# Bài 4: Infrastructure Layer — EF Core, Repositories & Aggregate Reconstitution

> **Dành cho**: Junior Developer muốn hiểu cách domain objects được lưu/load từ database
> **Thời gian đọc**: ~30 phút
> **Prerequisite**: Đã đọc [Bài 3: Application Layer](./03-application-layer.md)

## Bạn sẽ học được gì

- Infrastructure Layer làm gì — implement interfaces từ Domain/Application
- EF Core DbContext và entity mapping
- Value Object flattening — tại sao Money thành 2 cột trong DB
- Write Repository: cách save/load aggregate
- Read Repository: LINQ projections cho queries
- **Aggregate reconstitution** (phần khó nhất): `RuntimeHelpers.GetUninitializedObject()` và reflection
- Dependency Injection extensions

---

## Infrastructure Layer làm gì?

Infrastructure là layer **"bẩn"** nhất — nơi chứa tất cả code phụ thuộc framework bên ngoài:

- **EF Core** — ORM để nói chuyện với PostgreSQL
- **BCrypt** — hash password
- **JWT** — tạo và validate token
- **Repositories** — implement `IAccountRepository`, `IUserRepository`

Domain và Application define **"tôi cần gì"** (interfaces). Infrastructure provide **"đây, dùng cái này"** (implementations).

```
Application: "Tôi cần IPasswordHasher.Hash(password)"
Infrastructure: "OK, tôi cho BcryptPasswordHasher.Hash() dùng BCrypt workFactor 12"
```

---

## EF Core DbContext

### AccountsDbContext

**File**: `src/services/FinCore.Accounts/FinCore.Accounts.Infrastructure/Persistence/AccountsDbContext.cs`

```csharp
public class AccountsDbContext : DbContext
{
    public AccountsDbContext(DbContextOptions<AccountsDbContext> options)
        : base(options) { }

    public DbSet<AccountEntity> Accounts => Set<AccountEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(
            typeof(AccountsDbContext).Assembly);
    }
}
```

**Giải thích:**
- `DbSet<AccountEntity> Accounts` — đại diện cho table `accounts` trong database.
- `ApplyConfigurationsFromAssembly()` — tự động tìm tất cả `IEntityTypeConfiguration<T>` trong assembly và áp dụng. Bạn không cần register từng config thủ công.

### Tại sao dùng `AccountEntity` thay vì `Account` aggregate trực tiếp?

```
Account (Domain)          AccountEntity (Infrastructure)
─────────────────         ──────────────────────────────
Guid Id                   Guid Id
Guid OwnerId              Guid OwnerId
AccountNumber AccountNum  string AccountNumber      ← Flattened!
AccountType Type          string AccountType        ← Enum → string
Money Balance             decimal BalanceAmount     ← Flattened!
                          string BalanceCurrency    ← Flattened!
string Currency           string Currency
AccountStatus Status      string Status             ← Enum → string
DateTimeOffset CreatedAt  DateTimeOffset CreatedAt
long Version              long Version
```

**Value Object Flattening**: Domain dùng `Money` (Amount + CurrencyCode), nhưng database cần **cột riêng** cho mỗi giá trị. Repository chịu trách nhiệm convert qua lại.

**Tại sao không map Account trực tiếp?**
- Account aggregate có domain methods, events, private fields — EF Core khó xử lý
- AccountEntity là **POCO thuần túy** (chỉ properties) — EF Core map dễ dàng
- Tách biệt domain model và persistence model → thay đổi domain không ảnh hưởng DB schema và ngược lại

---

## Entity Configuration — Fluent API

**File**: `Persistence/Configurations/AccountEntityConfiguration.cs`

```csharp
public class AccountEntityConfiguration : IEntityTypeConfiguration<AccountEntity>
{
    public void Configure(EntityTypeBuilder<AccountEntity> builder)
    {
        builder.ToTable("accounts");
        builder.HasKey(a => a.Id);

        builder.Property(a => a.Id).ValueGeneratedNever();
        builder.Property(a => a.AccountNumber).HasMaxLength(30).IsRequired();
        builder.Property(a => a.AccountType).HasMaxLength(50).IsRequired();
        builder.Property(a => a.BalanceAmount).HasColumnType("decimal(18,4)");
        builder.Property(a => a.BalanceCurrency).HasMaxLength(3).IsRequired();
        builder.Property(a => a.Currency).HasMaxLength(3).IsRequired();
        builder.Property(a => a.Status).HasMaxLength(20).IsRequired();
        builder.Property(a => a.Version).IsConcurrencyToken();

        builder.HasIndex(a => a.AccountNumber).IsUnique();
        builder.HasIndex(a => a.OwnerId);
    }
}
```

### Giải thích từng dòng

**`builder.ToTable("accounts")`** — Table tên `accounts` (lowercase, theo PostgreSQL convention).

**`builder.Property(a => a.Id).ValueGeneratedNever()`** — ID do domain tạo (Guid.NewGuid()), KHÔNG do database auto-generate. Quan trọng vì aggregate tạo ID trong factory method.

**`builder.Property(a => a.BalanceAmount).HasColumnType("decimal(18,4)")`** — Độ chính xác cho tiền tệ: 18 chữ số tổng, 4 chữ số thập phân. VD: 99999999999999.9999. Trong tài chính, độ chính xác rất quan trọng.

**`builder.Property(a => a.Version).IsConcurrencyToken()`** — **Optimistic concurrency**. Nếu 2 requests cùng sửa một account:
1. Request A đọc Version = 5
2. Request B đọc Version = 5
3. Request A save với Version = 5 → thành công, Version → 6
4. Request B save với Version = 5 → FAIL (database thấy Version đã là 6)

Cơ chế này ngăn **lost updates** — vấn đề phổ biến trong hệ thống concurrent.

**`builder.HasIndex(a => a.AccountNumber).IsUnique()`** — AccountNumber phải unique trong toàn bộ table.

**`builder.HasIndex(a => a.OwnerId)`** — Index cho OwnerId vì query "lấy tất cả accounts của user X" rất phổ biến.

---

## Write Repository — EfAccountRepository

**File**: `Persistence/Repositories/EfAccountRepository.cs`

### AddAsync — Thêm account mới

```csharp
public async Task AddAsync(Account account, CancellationToken ct = default)
{
    var entity = MapToEntity(account);
    await _context.Accounts.AddAsync(entity, ct);
    await _context.SaveChangesAsync(ct);
}

private static AccountEntity MapToEntity(Account account) => new()
{
    Id = account.Id,
    OwnerId = account.OwnerId,
    AccountNumber = account.AccountNumber.Value,        // VO → string
    AccountType = account.AccountType.ToString(),        // Enum → string
    BalanceAmount = account.Balance.Amount,               // Money → decimal
    BalanceCurrency = account.Balance.CurrencyCode,      // Money → string
    Currency = account.Currency,
    Status = account.Status.ToString(),                  // Enum → string
    CreatedAt = account.CreatedAt,
    Version = account.Version
};
```

**MapToEntity** "flatten" domain aggregate thành entity phẳng mà EF Core hiểu.

### UpdateAsync — Cập nhật account

```csharp
public async Task UpdateAsync(Account account, CancellationToken ct = default)
{
    var entity = await _context.Accounts.FindAsync(new object[] { account.Id }, ct);
    if (entity is null) return;

    entity.BalanceAmount = account.Balance.Amount;
    entity.BalanceCurrency = account.Balance.CurrencyCode;
    entity.Status = account.Status.ToString();
    entity.Version = account.Version;

    await _context.SaveChangesAsync(ct);
}
```

Chỉ update các trường có thể thay đổi (Balance, Status, Version). AccountNumber, OwnerId, AccountType, CreatedAt **không bao giờ thay đổi** sau khi tạo.

### GetByIdAsync — Load aggregate (phần khó nhất!)

```csharp
public async Task<Account?> GetByIdAsync(Guid id, CancellationToken ct = default)
{
    var entity = await _context.Accounts.FindAsync(new object[] { id }, ct);
    return entity is null ? null : MapToDomain(entity);
}
```

`MapToDomain` là nơi **magic** xảy ra — convert entity phẳng thành domain aggregate. Đây là phần phức tạp nhất.

---

## Aggregate Reconstitution — Phần khó nhất

### Vấn đề

Account aggregate có:
- **Private constructor** — không gọi `new Account()` được
- **Factory method** `Account.Open()` — raise event `AccountOpened` → tạo new AccountNumber
- Khi load từ DB, ta KHÔNG muốn raise event hay tạo AccountNumber mới!

### Giải pháp: RuntimeHelpers.GetUninitializedObject()

```csharp
private static Account MapToDomain(AccountEntity entity)
{
    // Bước 1: Tạo instance Account MÀ KHÔNG gọi constructor
    var account = (Account)RuntimeHelpers.GetUninitializedObject(typeof(Account));

    // Bước 2: Khởi tạo _domainEvents (vì constructor bị skip)
    var domainEventsField = typeof(AggregateRoot)
        .GetField("_domainEvents", BindingFlags.NonPublic | BindingFlags.Instance);
    domainEventsField?.SetValue(account, new List<DomainEvent>());

    // Bước 3: Set tất cả properties qua reflection
    SetProperty(account, "Id", entity.Id);
    SetProperty(account, "OwnerId", entity.OwnerId);
    SetProperty(account, "AccountNumber", new AccountNumber(entity.AccountNumber));
    SetProperty(account, "AccountType", Enum.Parse<AccountType>(entity.AccountType));
    SetProperty(account, "Balance", new Money(entity.BalanceAmount, entity.BalanceCurrency));
    SetProperty(account, "Currency", entity.Currency);
    SetProperty(account, "Status", Enum.Parse<AccountStatus>(entity.Status));
    SetProperty(account, "CreatedAt", entity.CreatedAt);

    return account;
}
```

### Giải thích từng bước

**Bước 1: `RuntimeHelpers.GetUninitializedObject(typeof(Account))`**

Tạo một instance Account mà **KHÔNG chạy bất kỳ constructor nào**. Object được tạo ra có:
- Tất cả fields = default (null, 0, false...)
- KHÔNG có initialization code nào chạy
- KHÔNG có event nào được raise

**Tại sao không dùng factory `Account.Open()`?** Vì `Open()` sẽ:
- Tạo AccountNumber mới (ta muốn dùng AccountNumber từ DB)
- Raise `AccountOpened` event (ta không muốn event khi load)
- Tăng Version (ta muốn Version từ DB)

**Bước 2: Khởi tạo `_domainEvents`**

```csharp
// Trong AggregateRoot:
private readonly List<DomainEvent> _domainEvents = new();
```

Dòng `= new()` này KHÔNG chạy khi dùng `GetUninitializedObject()`. Nên `_domainEvents` = null!

Nếu không khởi tạo lại → gọi `account.Deposit()` → `RaiseEvent()` → `_domainEvents.Add()` → **NullReferenceException** !

Đây là **bug thực tế** đã gặp khi phát triển FinCore (xem `docs/phase-1-journey.md`).

**Bước 3: Set properties qua reflection**

```csharp
private static void SetProperty(object obj, string propertyName, object value)
{
    var prop = obj.GetType().GetProperty(propertyName,
        BindingFlags.Public | BindingFlags.Instance);

    if (prop is null)
    {
        // Tìm ở base class (VD: Id nằm ở AggregateRoot, không phải Account)
        var type = obj.GetType().BaseType;
        while (type is not null)
        {
            prop = type.GetProperty(propertyName,
                BindingFlags.Public | BindingFlags.Instance);
            if (prop is not null) break;
            type = type.BaseType;
        }
    }

    prop?.SetValue(obj, value);
}
```

Vì properties có `private set`, code bình thường không thể gán giá trị. Reflection **bypass** access modifier và set trực tiếp.

**Walk up base classes** — `Id` và `Version` nằm ở `AggregateRoot`, không phải `Account`. Nên phải tìm lên base class.

### So sánh: Identity dùng cách khác

**EfUserRepository** không dùng `GetUninitializedObject`. Thay vào đó:

```csharp
private static User MapToDomain(UserEntity entity)
{
    var email = new Email(entity.Email);
    var passwordHash = new HashedPassword(entity.PasswordHash);
    var role = Enum.Parse<UserRole>(entity.Role);

    // Dùng factory method thật
    var user = User.Register(email, passwordHash, role);

    // Ghi đè Id (Register tạo Id mới, ta muốn Id từ DB)
    typeof(AggregateRoot).GetProperty("Id")?.SetValue(user, entity.Id);

    // Xóa events do Register raise
    user.ClearDomainEvents();

    // Set các trường khác qua reflection nếu cần
    if (!entity.IsActive)
        typeof(User).GetProperty("IsActive")?.SetValue(user, false);

    typeof(User).GetProperty("CreatedAt")?.SetValue(user, entity.CreatedAt);

    // Reconstitute refresh tokens...
    return user;
}
```

**Tại sao cách khác?** Vì `User.Register()` nhận đủ parameters cần thiết (email, passwordHash, role). Ta dùng factory thật, rồi chỉ cần override Id và clear events. Đơn giản hơn `GetUninitializedObject`.

Account không thể dùng cách này vì `Account.Open()` tự generate AccountNumber mới — không nhận AccountNumber từ bên ngoài.

---

## Read Repository — LINQ Projections

**File**: `Persistence/ReadModels/AccountReadRepository.cs`

### GetByIdAsync — Project trực tiếp thành DTO

```csharp
public async Task<AccountDto?> GetByIdAsync(Guid id, CancellationToken ct = default)
{
    return await _context.Accounts
        .Where(a => a.Id == id)
        .Select(a => new AccountDto(
            a.Id,
            a.OwnerId,
            a.AccountNumber,
            a.AccountType,
            a.BalanceAmount,
            a.Currency,
            a.Status,
            a.CreatedAt))
        .FirstOrDefaultAsync(ct);
}
```

### Tại sao dùng `.Select()` thay vì load entity rồi map?

**Cách chậm:**
```csharp
var entity = await _context.Accounts.FindAsync(id);
return new AccountDto(entity.Id, entity.OwnerId, ...);
// → SQL: SELECT * FROM accounts WHERE id = @id
// → Load TẤT CẢ cột, kể cả cột không cần
```

**Cách nhanh (FinCore dùng):**
```csharp
.Select(a => new AccountDto(a.Id, a.OwnerId, ...))
// → SQL: SELECT id, owner_id, ... FROM accounts WHERE id = @id
// → Chỉ SELECT cột cần thiết
```

EF Core translate `.Select()` thành SQL chỉ lấy cột cần → ít data transfer, nhanh hơn.

### GetByOwnerAsync — Pagination

```csharp
public async Task<PagedList<AccountSummaryDto>> GetByOwnerAsync(
    Guid ownerId, int pageNumber, int pageSize, CancellationToken ct = default)
{
    var query = _context.Accounts.Where(a => a.OwnerId == ownerId);

    // Đếm tổng (cho pagination metadata)
    var totalCount = await query.CountAsync(ct);

    // Lấy trang hiện tại
    var items = await query
        .OrderByDescending(a => a.CreatedAt)    // Mới nhất lên trước
        .Skip((pageNumber - 1) * pageSize)       // Bỏ qua trang trước
        .Take(pageSize)                          // Lấy đúng số lượng
        .Select(a => new AccountSummaryDto(
            a.Id, a.AccountNumber, a.AccountType,
            a.BalanceAmount, a.Currency, a.Status))
        .ToListAsync(ct);

    return new PagedList<AccountSummaryDto>(
        items.AsReadOnly(), totalCount, pageNumber, pageSize);
}
```

**Lưu ý**: Tất cả operations (Where, Skip, Take, Select) chạy ở **database side** — EF Core translate thành SQL. Không load 10,000 records rồi filter trong C#.

---

## Dependency Injection Extensions

**File**: `DependencyInjection/AccountsInfrastructureExtensions.cs`

```csharp
public static class AccountsInfrastructureExtensions
{
    public static IServiceCollection AddAccountsInfrastructure(
        this IServiceCollection services, IConfiguration config)
    {
        // Connection string: env var → appsettings → default fallback
        var connectionString = Environment.GetEnvironmentVariable("DB__ACCOUNTS")
            ?? config.GetConnectionString("AccountsDb")
            ?? "Host=localhost;Port=5433;Database=fincore_accounts;" +
               "Username=fincore;Password=fincore_dev";

        // Register DbContext
        services.AddDbContext<AccountsDbContext>(options =>
            options.UseNpgsql(connectionString));

        // Register repositories (scoped = mỗi request một instance)
        services.AddScoped<IAccountRepository, EfAccountRepository>();
        services.AddScoped<IAccountReadRepository, AccountReadRepository>();

        return services;
    }
}
```

### Connection string resolution chain

```
1. Environment variable: DB__ACCOUNTS
   → Nếu có → dùng luôn
2. appsettings.json: ConnectionStrings:AccountsDb
   → Nếu có → dùng
3. Default fallback: localhost:5433/fincore_accounts
   → Luôn hoạt động cho local dev
```

**Tại sao ưu tiên env var?** Vì:
- Production: secrets nằm trong env vars (hoặc Vault)
- CI/CD: mỗi environment có connection string khác
- Local dev: fallback luôn hoạt động, không cần config gì

### Scoped lifetime

`AddScoped` nghĩa là mỗi HTTP request tạo **một instance mới** của repository. Request A và request B có repository riêng → không share state → thread-safe.

---

## Identity Infrastructure — Điểm khác biệt

Identity Infrastructure phức tạp hơn vì cần thêm:

### JWT Token Service

```csharp
services.Configure<JwtSettings>(options =>
{
    options.Secret = Environment.GetEnvironmentVariable("JWT__SECRET")
        ?? "dev-secret-must-be-at-least-32-chars!!";
    options.Issuer = Environment.GetEnvironmentVariable("JWT__ISSUER")
        ?? "fincore-identity";
    // ...
});

services.AddScoped<IJwtTokenService, JwtTokenService>();
```

### Password Hasher

```csharp
services.AddScoped<IPasswordHasher, BcryptPasswordHasher>();
```

`BcryptPasswordHasher` dùng workFactor = 12 (~100ms per hash) — cân bằng giữa security và performance.

---

## Tóm tắt

1. **Infrastructure implement interfaces** từ Domain/Application — EF Core repositories, BCrypt, JWT
2. **Entity riêng biệt với Aggregate** — `AccountEntity` (flat POCO) khác `Account` (rich domain object)
3. **Value Object flattening** — Money → BalanceAmount + BalanceCurrency trong DB
4. **Aggregate reconstitution** dùng `RuntimeHelpers.GetUninitializedObject()` + reflection (Accounts) hoặc factory + reflection (Identity)
5. **Phải khởi tạo `_domainEvents`** sau `GetUninitializedObject()` — quên = NullReferenceException
6. **Read Repository** dùng LINQ `.Select()` projection — database chỉ trả về cột cần thiết
7. **DI extensions** đóng gói toàn bộ infrastructure registration — API layer chỉ gọi `AddAccountsInfrastructure()`
8. **Connection string**: env var → config → fallback — local dev luôn hoạt động

> **Tiếp theo**: [Bài 5: API Layer](./05-api-layer.md) — Controllers, middleware pipeline, và cách HTTP request được xử lý.
