# Bài 2: Domain Layer — Aggregates, Business Rules & Events

> **Dành cho**: Junior Developer muốn hiểu cách tổ chức business logic theo DDD
> **Thời gian đọc**: ~30 phút
> **Prerequisite**: Đã đọc [Bài 1: SharedKernel](./01-shared-kernel.md)

## Bạn sẽ học được gì

- Domain Layer chứa gì và **không** chứa gì
- Account aggregate: phân tích chi tiết từng method
- User aggregate: authentication logic trong domain
- `Apply()` pattern: tại sao state mutation phải tách riêng
- Value Objects cụ thể: Email, HashedPassword, AccountNumber
- Domain Events: tất cả events và ý nghĩa
- Domain Exceptions: xử lý lỗi business

---

## Domain Layer chứa gì?

Domain Layer là **trái tim** của mỗi service. Nó chứa:

- **Aggregates** — đối tượng chính với business logic (Account, User)
- **Value Objects** — giá trị immutable (Email, AccountNumber, HashedPassword)
- **Domain Events** — sự kiện mô tả thay đổi state
- **Domain Exceptions** — lỗi business rule
- **Enums** — các loại trạng thái (AccountType, AccountStatus, UserRole)
- **Repository interfaces** — interface để truy cập data (KHÔNG phải implementation)

### Domain Layer KHÔNG chứa gì?

- **KHÔNG** có database code (EF Core, SQL...)
- **KHÔNG** có HTTP code (controllers, middleware...)
- **KHÔNG** có external library (BCrypt, JWT...)
- **KHÔNG** có NuGet dependencies (chỉ reference SharedKernel)

**Tại sao?** Vì domain logic phải **thuần túy**. Nếu business rule "số dư không được âm" phụ thuộc vào EF Core, thì khi đổi database bạn phải sửa cả business logic — đó là coupling không cần thiết.

---

## Account Aggregate — Tài khoản ngân hàng

**File**: `src/services/FinCore.Accounts/FinCore.Accounts.Domain/Aggregates/Account.cs`

### Cấu trúc tổng thể

```csharp
public class Account : AggregateRoot
{
    // --- Properties (state) ---
    public Guid OwnerId { get; private set; }
    public AccountNumber AccountNumber { get; private set; }
    public AccountType AccountType { get; private set; }
    public Money Balance { get; private set; }
    public string Currency { get; private set; }
    public AccountStatus Status { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    // --- Private constructor ---
    private Account() { }

    // --- Factory method ---
    public static Account Open(...) { ... }

    // --- Domain methods (business operations) ---
    public void Deposit(...) { ... }
    public void Withdraw(...) { ... }
    public void Freeze(...) { ... }
    public void Unfreeze() { ... }
    public void Close() { ... }

    // --- Apply (state mutation) ---
    protected override void Apply(DomainEvent @event) { ... }
}
```

### Tại sao private constructor + static factory?

```csharp
private Account() { }  // Không ai có thể gọi new Account()

public static Account Open(Guid ownerId, AccountType accountType, string currency)
{
    var account = new Account();
    var accountNumber = AccountNumber.Generate();
    account.RaiseEvent(new AccountOpened(
        Guid.NewGuid(), ownerId, accountType.ToString(),
        currency, accountNumber.Value));
    return account;
}
```

**Private constructor** ngăn code bên ngoài tạo Account bằng `new Account()`. Bắt buộc phải đi qua `Account.Open()`.

**Tại sao?** Vì khi mở tài khoản, PHẢI:
1. Tạo AccountNumber mới
2. Raise event `AccountOpened`
3. Apply event để set initial state

Nếu cho phép `new Account()`, developer có thể tạo Account mà quên raise event → state không consistent.

### Deposit — Nạp tiền

```csharp
public void Deposit(decimal amount, string reference)
{
    if (Status != AccountStatus.Active)
        throw new AccountNotActiveException(Id, Status.ToString());
    if (amount <= 0)
        throw new DomainException("Deposit amount must be positive.");

    RaiseEvent(new MoneyDeposited(Id, amount, Currency, reference));
}
```

**Flow:**
1. **Kiểm tra trạng thái** — Chỉ tài khoản Active mới được nạp tiền. Tài khoản Frozen hoặc Closed → throw exception.
2. **Kiểm tra số tiền** — Phải dương. Nạp 0 hoặc -100 → throw exception.
3. **Raise event** — Nếu mọi thứ OK, tạo event `MoneyDeposited`. Event này sẽ được `Apply()` xử lý.

**Chú ý**: Method này KHÔNG trực tiếp sửa `Balance`. Việc sửa Balance nằm trong `Apply()`.

### Withdraw — Rút tiền

```csharp
public void Withdraw(decimal amount, string reference)
{
    if (Status != AccountStatus.Active)
        throw new AccountNotActiveException(Id, Status.ToString());
    if (amount <= 0)
        throw new DomainException("Withdrawal amount must be positive.");
    if (Balance.Amount < amount)
        throw new InsufficientFundsException(amount, Balance.Amount, Currency);

    RaiseEvent(new MoneyWithdrawn(Id, amount, Currency, reference));
}
```

So với Deposit, có thêm một business rule: **số dư phải đủ** (`Balance.Amount < amount`). Đây là invariant quan trọng nhất của tài khoản ngân hàng — không được rút quá số dư.

### Freeze — Đóng băng tài khoản

```csharp
public void Freeze(string reason)
{
    if (Status == AccountStatus.Closed)
        throw new AccountAlreadyClosedException(Id);
    if (Status == AccountStatus.Frozen)
        throw new DomainException($"Account '{Id}' is already frozen.");

    RaiseEvent(new AccountFrozen(Id, reason));
}
```

- Tài khoản đã đóng (Closed) → không thể freeze
- Tài khoản đã freeze → không thể freeze lần nữa
- `reason` — lý do đóng băng (để audit)
- Trong thực tế, Freeze thường do ComplianceOfficer hoặc Admin thực hiện (kiểm tra ở API layer)

### Close — Đóng tài khoản

```csharp
public void Close()
{
    if (Status == AccountStatus.Closed)
        throw new AccountAlreadyClosedException(Id);
    if (Balance.Amount != 0)
        throw new DomainException(
            $"Cannot close account '{Id}' with non-zero balance ({Balance}).");

    RaiseEvent(new AccountClosed(Id, DateTimeOffset.UtcNow));
}
```

**Business rule quan trọng**: Chỉ có thể đóng tài khoản khi **số dư = 0**. Phải rút hết tiền trước khi đóng — đây là requirement thực tế trong ngân hàng.

### Apply() — Nơi duy nhất state thay đổi

```csharp
protected override void Apply(DomainEvent @event)
{
    switch (@event)
    {
        case AccountOpened opened:
            Id = opened.AccountId;
            OwnerId = opened.OwnerId;
            AccountNumber = new AccountNumber(opened.AccountNumber);
            AccountType = Enum.Parse<AccountType>(opened.AccountType);
            Currency = opened.Currency;
            Balance = new Money(0, opened.Currency);
            Status = AccountStatus.Active;
            CreatedAt = opened.OccurredAt;
            break;

        case MoneyDeposited deposited:
            Balance = Balance + new Money(deposited.Amount, deposited.Currency);
            break;

        case MoneyWithdrawn withdrawn:
            Balance = Balance - new Money(withdrawn.Amount, withdrawn.Currency);
            break;

        case AccountFrozen:
            Status = AccountStatus.Frozen;
            break;

        case AccountUnfrozen:
            Status = AccountStatus.Active;
            break;

        case AccountClosed:
            Status = AccountStatus.Closed;
            break;
    }
}
```

### Tại sao tách Apply() ra riêng?

**Lý do 1: Single source of truth**
- Nếu bạn sửa Balance ở nhiều nơi (Deposit sửa một chỗ, Withdraw sửa chỗ khác), rất dễ quên hoặc sửa sai.
- Tất cả state mutation gom lại trong `Apply()` → dễ review, dễ debug.

**Lý do 2: Event Sourcing ready**
- Ở Phase 2, thay vì lưu state cuối cùng, ta lưu **chuỗi events**.
- Để tái tạo state, ta replay events qua `Apply()` theo thứ tự.
- VD: AccountOpened → MoneyDeposited(100) → MoneyWithdrawn(30) → Balance = 70

**Lý do 3: Testable**
- Test domain bằng cách gọi method → kiểm tra events + state.
- Không cần mock database vì `Apply()` là pure function.

---

## User Aggregate — Quản lý người dùng

**File**: `src/services/FinCore.Identity/FinCore.Identity.Domain/Aggregates/User.cs`

```csharp
public class User : AggregateRoot
{
    public Email Email { get; private set; }
    public HashedPassword PasswordHash { get; private set; }
    public UserRole Role { get; private set; }
    public bool IsActive { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    private readonly List<RefreshToken> _refreshTokens = new();
    public IReadOnlyList<RefreshToken> RefreshTokens => _refreshTokens.AsReadOnly();

    private User() { }

    public static User Register(Email email, HashedPassword passwordHash, UserRole role)
    {
        var user = new User();
        user.RaiseEvent(new UserRegistered(Guid.NewGuid(), email.Value, role.ToString()));
        user.Email = email;
        user.PasswordHash = passwordHash;
        user.Role = role;
        return user;
    }
}
```

### Điểm khác biệt so với Account

1. **User.Register() set properties trực tiếp** ngoài `Apply()` — Email, PasswordHash, Role được set ngay trong factory method, không hoàn toàn qua Apply.
2. **User quản lý RefreshToken** — danh sách token dùng cho authentication.
3. **User có concept "active/deactivate"** — tài khoản user có thể bị vô hiệu hóa.

### Login — Business rule trong domain

```csharp
public void Login()
{
    if (!IsActive)
        throw new UserDeactivatedException(Id);
    RaiseEvent(new UserLoggedIn(Id, DateTimeOffset.UtcNow));
}
```

**Chú ý**: `Login()` KHÔNG kiểm tra mật khẩu. Tại sao? Vì:
- Password verification cần BCrypt (external library) → thuộc về Infrastructure.
- Domain chỉ kiểm tra **business rules** (user phải active).
- Application layer sẽ verify password trước, rồi mới gọi `user.Login()`.

### RefreshToken management

```csharp
public RefreshToken AddRefreshToken(string tokenHash, DateTimeOffset expiresAt)
{
    var token = new RefreshToken(tokenHash, expiresAt);
    _refreshTokens.Add(token);
    return token;
}

public RefreshToken? GetActiveRefreshToken(string tokenHash) =>
    _refreshTokens.FirstOrDefault(t => t.TokenHash == tokenHash && t.IsActive);

public void RevokeAllRefreshTokens()
{
    foreach (var token in _refreshTokens.Where(t => t.IsActive))
        token.Revoke();
}
```

User aggregate quản lý refresh tokens vì tokens **thuộc về user** — chúng là một phần của user aggregate boundary.

---

## Value Objects trong Domain

### Email (Identity)

**File**: `src/services/FinCore.Identity/FinCore.Identity.Domain/ValueObjects/Email.cs`

```csharp
public sealed class Email : ValueObject
{
    public string Value { get; }

    public Email(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new DomainException("Email cannot be empty.");

        if (!value.Contains('@') || !value.Contains('.'))
            throw new DomainException($"'{value}' is not a valid email address.");

        Value = value.Trim().ToLowerInvariant();  // Normalize
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Value;
    }
}
```

**Đặc điểm:**
- **Validation on construction** — Không thể tạo Email invalid.
- **Normalization** — Luôn lowercase: `User@Example.COM` → `user@example.com`.
- **Immutable** — Không có setter, không thể thay đổi sau khi tạo.

### HashedPassword (Identity)

```csharp
public sealed class HashedPassword : ValueObject
{
    public string Value { get; }

    public HashedPassword(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new DomainException("Password hash cannot be empty.");
        Value = value;
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Value;
    }
}
```

**Quan trọng**: HashedPassword chỉ là **wrapper cho string** — nó KHÔNG chứa logic hash (BCrypt). Tại sao?
- BCrypt là library bên ngoài → thuộc Infrastructure.
- Domain chỉ biết "có một chuỗi hash" — không quan tâm hash bằng gì.
- Nếu ngày mai đổi từ BCrypt sang Argon2, Domain **không cần sửa**.

### AccountNumber (Accounts)

**File**: `src/services/FinCore.Accounts/FinCore.Accounts.Domain/ValueObjects/AccountNumber.cs`

```csharp
public sealed class AccountNumber : ValueObject
{
    public string Value { get; }

    public AccountNumber(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new DomainException("Account number cannot be empty.");
        Value = value;
    }

    public static AccountNumber Generate()
    {
        var timestamp = DateTimeOffset.UtcNow.ToString("yyyyMMddHHmmss");
        var random = Random.Shared.Next(1000, 9999);
        return new AccountNumber($"FC-{timestamp}-{random}");
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Value;
    }
}
```

- `Generate()` tạo số tài khoản mới: `FC-20260306143022-5847`
- Format: `FC-{timestamp}-{random}` — đảm bảo unique trong thực tế.
- Được gọi trong `Account.Open()` factory method.

---

## Domain Events — Tất cả events

### Accounts Domain Events

| Event | Khi nào | Dữ liệu |
|---|---|---|
| `AccountOpened` | Mở TK mới | AccountId, OwnerId, AccountType, Currency, AccountNumber |
| `MoneyDeposited` | Nạp tiền | AccountId, Amount, Currency, Reference |
| `MoneyWithdrawn` | Rút tiền | AccountId, Amount, Currency, Reference |
| `AccountFrozen` | Đóng băng TK | AccountId, Reason |
| `AccountUnfrozen` | Mở đóng băng | AccountId |
| `AccountClosed` | Đóng TK | AccountId, ClosedAt |

### Identity Domain Events

| Event | Khi nào | Dữ liệu |
|---|---|---|
| `UserRegistered` | Đăng ký mới | UserId, Email, Role |
| `UserLoggedIn` | Đăng nhập thành công | UserId, LoggedInAt |
| `UserDeactivated` | Vô hiệu hóa TK | UserId, DeactivatedAt |
| `UserRoleChanged` | Thay đổi role | UserId, OldRole, NewRole |

**Quy tắc đặt tên event**: Luôn dùng **quá khứ phân từ** (past participle) — mô tả điều **đã xảy ra**: `AccountOpened` (không phải `OpenAccount`), `MoneyDeposited` (không phải `DepositMoney`).

---

## Domain Exceptions — Custom exceptions

### Accounts Exceptions

```csharp
// Khi tài khoản không active (Frozen/Closed) mà muốn Deposit/Withdraw
public class AccountNotActiveException : DomainException
{
    public AccountNotActiveException(Guid accountId, string status)
        : base($"Account '{accountId}' is not active. Current status: {status}.") { }
}

// Khi tài khoản đã đóng mà muốn Freeze/Close
public class AccountAlreadyClosedException : DomainException
{
    public AccountAlreadyClosedException(Guid accountId)
        : base($"Account '{accountId}' is already closed.") { }
}

// Khi rút tiền nhiều hơn số dư
public class InsufficientFundsException : DomainException
{
    public InsufficientFundsException(decimal requested, decimal available, string currency)
        : base($"Insufficient funds. Requested: {requested:F2} {currency}, " +
               $"Available: {available:F2} {currency}.") { }
}
```

**Tại sao không dùng chung `DomainException`?**
- Custom exception cho phép **bắt riêng** (VD: `catch (InsufficientFundsException)`)
- Message rõ ràng, chứa context (accountId, amounts)
- Dễ search trong logs
- Middleware có thể map khác nhau nếu cần

---

## Enums — Trạng thái và loại

```csharp
// AccountType — Loại tài khoản
public enum AccountType { Checking = 0, Savings = 1, Investment = 2 }

// AccountStatus — Trạng thái tài khoản
public enum AccountStatus { Active, Frozen, Closed }

// UserRole — Vai trò người dùng
public enum UserRole { Customer = 0, Analyst = 1, ComplianceOfficer = 2, Admin = 3 }
```

**Cách lưu trong database**: Enums được lưu dưới dạng **string** (không phải int). VD: `"Active"` thay vì `0`. Tại sao?
- Dễ đọc khi query database trực tiếp
- Không bị "magic number" — nhìn vào DB biết ngay trạng thái
- Migration-safe — thêm enum value mới không ảnh hưởng giá trị cũ

---

## Repository Interfaces — Định nghĩa ở Domain

```csharp
// File: FinCore.Accounts.Domain/Repositories/IAccountRepository.cs
public interface IAccountRepository
{
    Task<Account?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<Account>> GetByOwnerIdAsync(Guid ownerId, CancellationToken ct = default);
    Task AddAsync(Account account, CancellationToken ct = default);
    Task UpdateAsync(Account account, CancellationToken ct = default);
}
```

**Tại sao interface ở Domain nhưng implementation ở Infrastructure?**

Đây là **Dependency Inversion Principle** (chữ D trong SOLID):
- Domain define "tôi cần gì" (interface)
- Infrastructure provide "tôi cho bạn cái này" (implementation)
- Domain KHÔNG biết ai implement — nó chỉ biết interface

```
Domain: "Tôi cần IAccountRepository"
    ↑
Infrastructure: "Đây, tôi có EfAccountRepository dùng EF Core"
```

Nếu ngày mai đổi sang MongoDB, bạn tạo `MongoAccountRepository` implement `IAccountRepository` — Domain **không thay đổi gì**.

---

## State Machine — Trạng thái tài khoản

Account có một state machine ngầm:

```
    Open()
      │
      ▼
  ┌─────────┐
  │  Active  │ ◄──── Unfreeze()
  └────┬─────┘
       │
  ┌────┴─────┬──────────┐
  │          │          │
Deposit() Withdraw() Freeze()
  │          │          │
  ▼          ▼          ▼
(Balance+) (Balance-) ┌────────┐
                      │ Frozen │
                      └────────┘
       │
   Close() (Balance phải = 0)
       │
       ▼
  ┌────────┐
  │ Closed │ (terminal state — không thể quay lại)
  └────────┘
```

**Rules:**
- `Active` → có thể Deposit, Withdraw, Freeze, Close
- `Frozen` → chỉ có thể Unfreeze (không Deposit/Withdraw/Close)
- `Closed` → không thể làm gì (terminal state)

---

## Tóm tắt

1. **Domain Layer = business logic thuần túy** — không phụ thuộc framework
2. **Aggregates** dùng factory methods thay vì constructor, enforce business rules trước khi raise event
3. **Apply()** là single source of truth cho state mutation — chuẩn bị cho Event Sourcing
4. **Value Objects** (Email, Money, AccountNumber) validate trên construction, immutable, so sánh bằng value
5. **Domain Events** đặt tên bằng past tense, mô tả điều đã xảy ra
6. **Repository interfaces** define ở Domain nhưng implement ở Infrastructure (Dependency Inversion)
7. **Business rules** nằm trong aggregate methods — controller và handler không chứa business logic

> **Tiếp theo**: [Bài 3: Application Layer](./03-application-layer.md) — Cách CQRS và MediatR điều phối workflow giữa API và Domain.
