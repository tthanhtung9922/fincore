# Bài 1: SharedKernel — Nền Tảng DDD

> **Dành cho**: Junior Developer muốn hiểu các building blocks cốt lõi của Domain-Driven Design
> **Thời gian đọc**: ~25 phút
> **Prerequisite**: Đã đọc [Bài 0: Tổng Quan](./00-overview.md)

## Bạn sẽ học được gì

- SharedKernel là gì và tại sao cần nó
- `AggregateRoot` — trái tim của DDD, cách `RaiseEvent()` và `Apply()` hoạt động
- `ValueObject` — tại sao equality by value quan trọng
- `Entity` — khác ValueObject chỗ nào
- `DomainEvent` — sự kiện trong domain
- `Result<T>` — tại sao không dùng exception cho business logic
- `Money` — value object với operator overloading

---

## SharedKernel là gì?

SharedKernel (hạt nhân dùng chung) là tập hợp các **base class và building blocks** mà TẤT CẢ microservices đều dùng. Thay vì mỗi service tự viết `AggregateRoot`, `ValueObject`..., ta đặt chúng ở một nơi và reference tới.

```
src/shared/FinCore.SharedKernel/
├── Common/
│   └── Result.cs              # Result<T> pattern
├── Domain/
│   ├── AggregateRoot.cs       # Base class cho aggregate
│   ├── DomainEvent.cs         # Base record cho domain event
│   ├── DomainException.cs     # Base exception cho domain
│   ├── Entity.cs              # Base class cho entity
│   ├── ValueObject.cs         # Base class cho value object
│   └── ValueObjects/
│       ├── Money.cs           # Tiền tệ
│       └── Iban.cs            # Số tài khoản IBAN
└── Pagination/
    └── PagedList.cs           # Kết quả phân trang
```

**Đặc biệt**: SharedKernel có **ZERO NuGet dependencies** — không phụ thuộc bất kỳ thư viện bên ngoài nào. Chỉ dùng C# thuần túy. Đây là thiết kế có chủ đích — domain code không nên bị ràng buộc bởi framework.

---

## 1. AggregateRoot — Trái tim của DDD

### Aggregate Root là gì?

Hãy tưởng tượng bạn có một **tài khoản ngân hàng**. Tài khoản đó có: số tài khoản, số dư, loại tài khoản, trạng thái... Tất cả những thứ này **thuộc về tài khoản** — bạn không bao giờ thay đổi số dư mà không thông qua tài khoản.

Aggregate Root là **"người gác cổng"** — mọi thay đổi trạng thái PHẢI đi qua nó. Không ai được phép sửa số dư trực tiếp, phải gọi `account.Deposit(100)`.

### Code thực tế

**File**: `src/shared/FinCore.SharedKernel/Domain/AggregateRoot.cs`

```csharp
public abstract class AggregateRoot
{
    public Guid Id { get; protected set; }
    public long Version { get; private set; }

    private readonly List<DomainEvent> _domainEvents = new();
    public IReadOnlyList<DomainEvent> DomainEvents => _domainEvents.AsReadOnly();

    protected void RaiseEvent(DomainEvent @event)
    {
        _domainEvents.Add(@event);
        Apply(@event);
        Version++;
    }

    protected virtual void Apply(DomainEvent @event) { }

    public void ClearDomainEvents() => _domainEvents.Clear();
}
```

### Giải thích từng dòng

**`public Guid Id { get; protected set; }`**
- Mỗi aggregate có một ID duy nhất (kiểu `Guid`).
- `protected set` nghĩa là chỉ class con (VD: `Account`, `User`) mới có thể set giá trị, code bên ngoài không thể sửa ID.

**`public long Version { get; private set; }`**
- Đếm số lần aggregate thay đổi trạng thái.
- Dùng cho **optimistic concurrency** — nếu 2 request cùng sửa một aggregate, Version giúp phát hiện xung đột.
- `private set` — chỉ class này mới tăng Version (trong `RaiseEvent`).

**`private readonly List<DomainEvent> _domainEvents = new();`**
- Danh sách các sự kiện đã xảy ra trong aggregate.
- `private` — code bên ngoài không thể truy cập trực tiếp.
- `readonly` — không thể gán lại list (nhưng vẫn có thể thêm/xóa phần tử).

**`public IReadOnlyList<DomainEvent> DomainEvents => _domainEvents.AsReadOnly();`**
- Cho phép code bên ngoài **đọc** danh sách events, nhưng KHÔNG thể thêm/xóa.
- Đây là **encapsulation** — bảo vệ dữ liệu nội bộ.

**`protected void RaiseEvent(DomainEvent @event)`**
- Đây là method CỐT LÕI. Khi aggregate thay đổi trạng thái, nó gọi `RaiseEvent()` với một event mô tả thay đổi.
- `@event` — dấu `@` cho phép dùng keyword `event` làm tên biến (vì `event` là keyword trong C#).
- Method này làm 3 việc theo thứ tự:
  1. **Thêm event vào danh sách** — `_domainEvents.Add(@event)`
  2. **Apply event để cập nhật state** — `Apply(@event)`
  3. **Tăng version** — `Version++`

**`protected virtual void Apply(DomainEvent @event) { }`**
- Method rỗng, được **override** bởi class con để xử lý từng loại event.
- `virtual` nghĩa là class con có thể ghi đè.
- Đây là nơi DUY NHẤT state được cập nhật — **single source of truth**.

**`public void ClearDomainEvents()`**
- Xóa toàn bộ events sau khi đã lưu xong vào database.
- Sẽ được gọi bởi command handler sau khi persist aggregate.

### Tại sao phải dùng RaiseEvent + Apply thay vì set trực tiếp?

**Cách "naive" (cách bạn thường viết):**
```csharp
public void Deposit(decimal amount)
{
    Balance += amount;  // Set trực tiếp
}
```

**Cách DDD (FinCore dùng):**
```csharp
public void Deposit(decimal amount, string reference)
{
    // 1. Validate business rules
    if (Status != AccountStatus.Active)
        throw new AccountNotActiveException(Id, Status.ToString());
    if (amount <= 0)
        throw new DomainException("Deposit amount must be positive.");

    // 2. Raise event (state sẽ được cập nhật trong Apply)
    RaiseEvent(new MoneyDeposited(Id, amount, Currency, reference));
}

protected override void Apply(DomainEvent @event)
{
    switch (@event)
    {
        case MoneyDeposited deposited:
            Balance = Balance + new Money(deposited.Amount, deposited.Currency);
            break;
    }
}
```

**Lợi ích của cách DDD:**

1. **Audit trail miễn phí** — Bạn luôn biết "chuyện gì đã xảy ra" vì mỗi thay đổi đều có event.
2. **Event Sourcing ready** — Ở Phase 2+, ta có thể replay events để tái tạo state.
3. **Testable** — Test bằng cách kiểm tra events được raise, không cần mock database.
4. **Single source of truth** — Mọi state mutation đều đi qua `Apply()`, không có "đường tắt".

---

## 2. ValueObject — Giá trị, không phải đối tượng

### Value Object là gì?

Hãy nghĩ về **số tiền 100 USD**. Bạn không quan tâm "tờ 100 USD nào" (nó không có identity), bạn chỉ quan tâm **giá trị** là 100 USD. Hai tờ 100 USD là **bằng nhau** — đây là value object.

Ngược lại, **tài khoản ngân hàng** có identity (số tài khoản) — hai tài khoản khác nhau dù cùng số dư.

### Code thực tế

**File**: `src/shared/FinCore.SharedKernel/Domain/ValueObject.cs`

```csharp
public abstract class ValueObject
{
    protected abstract IEnumerable<object?> GetEqualityComponents();

    public override bool Equals(object? obj)
    {
        if (obj is null || obj.GetType() != GetType())
            return false;

        return GetEqualityComponents()
            .SequenceEqual(((ValueObject)obj).GetEqualityComponents());
    }

    public override int GetHashCode() =>
        GetEqualityComponents()
            .Aggregate(1, (current, obj) =>
                current * 23 + (obj?.GetHashCode() ?? 0));

    public static bool operator ==(ValueObject? a, ValueObject? b)
    {
        if (a is null && b is null) return true;
        if (a is null || b is null) return false;
        return a.Equals(b);
    }

    public static bool operator !=(ValueObject? a, ValueObject? b) => !(a == b);
}
```

### Giải thích

**`GetEqualityComponents()`**
- Mỗi value object phải liệt kê các "thành phần" dùng để so sánh.
- VD: `Money` trả về `[Amount, CurrencyCode]` — hai Money bằng nhau khi cả Amount và CurrencyCode giống nhau.

**`Equals()` và `GetHashCode()`**
- Override behavior mặc định của C#.
- Mặc định, C# so sánh object bằng **reference** (cùng vùng nhớ = bằng nhau).
- ValueObject so sánh bằng **value** (cùng giá trị = bằng nhau).

**`operator ==` và `!=`**
- Cho phép dùng `money1 == money2` thay vì `money1.Equals(money2)`.

### So sánh: reference equality vs value equality

```csharp
// Reference equality (mặc định C# cho class)
var a = new object();
var b = new object();
a == b  // FALSE — khác vùng nhớ dù cùng kiểu

// Value equality (ValueObject)
var money1 = new Money(100, "USD");
var money2 = new Money(100, "USD");
money1 == money2  // TRUE — cùng Amount và CurrencyCode
```

---

## 3. Entity — Đối tượng có danh tính

### Code thực tế

**File**: `src/shared/FinCore.SharedKernel/Domain/Entity.cs`

```csharp
public abstract class Entity
{
    public Guid Id { get; protected set; }

    public override bool Equals(object? obj)
    {
        if (obj is not Entity other)
            return false;
        if (ReferenceEquals(this, other))
            return true;
        if (GetType() != other.GetType())
            return false;
        return Id == other.Id;
    }

    public override int GetHashCode() => Id.GetHashCode();

    public static bool operator ==(Entity? a, Entity? b)
    {
        if (a is null && b is null) return true;
        if (a is null || b is null) return false;
        return a.Equals(b);
    }

    public static bool operator !=(Entity? a, Entity? b) => !(a == b);
}
```

### Entity vs ValueObject

| | Entity | ValueObject |
|---|---|---|
| **Identity** | Có (Guid Id) | Không |
| **Equality** | So sánh bằng Id | So sánh bằng tất cả components |
| **Mutable?** | Có thể thay đổi (qua domain methods) | Immutable (bất biến) |
| **VD trong FinCore** | `RefreshToken` (có Id riêng) | `Money`, `Email`, `AccountNumber` |

**Lưu ý**: `AggregateRoot` không kế thừa `Entity` trong project này — chúng là hai base class riêng biệt. AggregateRoot là "gốc" quản lý cả aggregate, Entity là thành phần bên trong aggregate.

---

## 4. DomainEvent — Ghi nhận những gì đã xảy ra

### Code thực tế

**File**: `src/shared/FinCore.SharedKernel/Domain/DomainEvent.cs`

```csharp
public record DomainEvent
{
    public Guid EventId { get; init; } = Guid.NewGuid();
    public DateTimeOffset OccurredAt { get; init; } = DateTimeOffset.UtcNow;
    public int SchemaVersion { get; init; } = 1;
}
```

### Tại sao dùng `record` thay vì `class`?

`record` trong C# là kiểu dữ liệu **immutable by default** với equality by value. Hoàn hảo cho events vì:

1. **Immutable** — Event đã xảy ra rồi, không ai được phép sửa.
2. **Value equality** — Hai event cùng data là bằng nhau.
3. **Concise** — Ít boilerplate code hơn class.

### Ví dụ event thực tế

```csharp
// File: FinCore.Accounts.Domain/Events/MoneyDeposited.cs
public record MoneyDeposited(
    Guid AccountId,
    decimal Amount,
    string Currency,
    string Reference) : DomainEvent;
```

Event này nói: **"Tài khoản X đã được nạp Y đồng Z, với mã giao dịch W"**. Nó mô tả **điều đã xảy ra** (past tense), không phải yêu cầu (command).

---

## 5. DomainException — Lỗi business logic

**File**: `src/shared/FinCore.SharedKernel/Domain/DomainException.cs`

```csharp
public class DomainException : Exception
{
    public DomainException(string message) : base(message) { }
    public DomainException(string message, Exception innerException)
        : base(message, innerException) { }
}
```

### Tại sao cần DomainException riêng?

- **Phân biệt lỗi business vs lỗi hệ thống** — `DomainException` (VD: "Số dư không đủ") khác hoàn toàn `NullReferenceException` (bug code).
- **Middleware xử lý khác nhau** — `DomainException` → HTTP 400 (Bad Request), exception khác → HTTP 500 (Internal Server Error).
- **Không lộ thông tin nhạy cảm** — lỗi 500 trả về message generic, lỗi 400 trả về message cụ thể cho client.

---

## 6. Result\<T\> — Thay thế exception cho business logic

### Vấn đề với exception

```csharp
// Cách "naive" — dùng exception cho business logic
public Guid RegisterUser(string email)
{
    if (userExists(email))
        throw new UserAlreadyExistsException(email);  // Exception = expensive!
    // ...
}
```

Exception rất **tốn performance** (stack unwinding) và khó control flow. Khi bạn có 10 business rules, mỗi cái throw exception khác nhau, code trở nên khó đọc.

### Code thực tế

**File**: `src/shared/FinCore.SharedKernel/Common/Result.cs`

```csharp
public class Result
{
    public bool IsSuccess { get; }
    public string? Error { get; }
    public bool IsFailure => !IsSuccess;

    protected Result(bool isSuccess, string? error)
    {
        IsSuccess = isSuccess;
        Error = error;
    }

    public static Result Success() => new(true, null);
    public static Result Failure(string error) => new(false, error);
    public static Result<T> Success<T>(T value) => Result<T>.Success(value);
    public static Result<T> Failure<T>(string error) => Result<T>.Failure(error);
}

public class Result<T> : Result
{
    private readonly T? _value;

    public T Value => IsSuccess
        ? _value!
        : throw new InvalidOperationException(
            "Cannot access Value on a failed Result.");

    private Result(bool isSuccess, T? value, string? error)
        : base(isSuccess, error)
    {
        _value = value;
    }

    public static Result<T> Success(T value) => new(true, value, null);
    public new static Result<T> Failure(string error) => new(false, default, error);
}
```

### Cách sử dụng

```csharp
// Trong command handler
public async Task<Result<Guid>> Handle(RegisterUserCommand request, ...)
{
    var existingUser = await _repo.GetByEmailAsync(email, ct);
    if (existingUser is not null)
        return Result.Failure<Guid>("User already exists.");  // Không throw!

    var user = User.Register(email, hashedPassword, role);
    await _repo.AddAsync(user, ct);
    return Result.Success(user.Id);  // Trả về kết quả thành công
}

// Trong controller
var result = await _mediator.Send(command);
if (!result.IsSuccess)
    return BadRequest(new { error = result.Error });

return CreatedAtAction(..., new { id = result.Value });
```

### Result vs Exception — khi nào dùng cái nào?

| Tình huống | Dùng | Lý do |
|---|---|---|
| User đã tồn tại | `Result.Failure()` | Business logic, expected case |
| Email format sai | `DomainException` | Domain invariant bị vi phạm |
| Database connection failed | `Exception` (tự nhiên) | Infrastructure error, unexpected |
| Số dư không đủ | `DomainException` | Business rule violation |

**Quy tắc chung**: `Result` cho expected failures ở Application layer, `DomainException` cho invariant violations ở Domain layer.

---

## 7. Money — Value Object thực tế

### Code thực tế

**File**: `src/shared/FinCore.SharedKernel/Domain/ValueObjects/Money.cs`

```csharp
public sealed class Money : ValueObject
{
    public decimal Amount { get; }
    public string CurrencyCode { get; }

    public Money(decimal amount, string currencyCode)
    {
        if (string.IsNullOrWhiteSpace(currencyCode) || currencyCode.Length != 3)
            throw new ArgumentException(
                "Currency code must be a 3-letter ISO 4217 code.",
                nameof(currencyCode));

        Amount = amount;
        CurrencyCode = currencyCode.ToUpperInvariant();
    }

    public static Money operator +(Money a, Money b)
    {
        if (a.CurrencyCode != b.CurrencyCode)
            throw new InvalidOperationException(
                $"Cannot add amounts with different currencies: " +
                $"{a.CurrencyCode} and {b.CurrencyCode}.");
        return new Money(a.Amount + b.Amount, a.CurrencyCode);
    }

    public static Money operator -(Money a, Money b)
    {
        if (a.CurrencyCode != b.CurrencyCode)
            throw new InvalidOperationException(
                $"Cannot subtract amounts with different currencies: " +
                $"{a.CurrencyCode} and {b.CurrencyCode}.");
        return new Money(a.Amount - b.Amount, a.CurrencyCode);
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Amount;
        yield return CurrencyCode;
    }

    public override string ToString() => $"{Amount:F2} {CurrencyCode}";
}
```

### Phân tích từng điểm

**`sealed`** — Không ai có thể kế thừa Money. Value objects nên là sealed vì equality logic dựa trên type cụ thể.

**Validation trong constructor:**
```csharp
if (string.IsNullOrWhiteSpace(currencyCode) || currencyCode.Length != 3)
    throw new ArgumentException(...);
```
- Money luôn **valid** ngay khi tạo ra. Không thể tồn tại Money với currency code rỗng.
- Đây gọi là **invariant** — điều kiện LUÔN đúng trong suốt vòng đời object.

**`ToUpperInvariant()`** — Normalize currency code thành uppercase ("usd" → "USD"). Đảm bảo consistency.

**Operator overloading (`+` và `-`):**
```csharp
var balance = new Money(100, "USD");
var deposit = new Money(50, "USD");
var newBalance = balance + deposit;  // Money(150, "USD")
```
- Chỉ cho phép cộng/trừ cùng loại tiền. `USD + EUR` sẽ throw exception.
- Trả về **Money MỚI** (immutable) — không sửa Money cũ.

**Tại sao không dùng `decimal` đơn giản?**

```csharp
// Cách "naive" — chỉ dùng decimal
decimal balance = 100;
decimal deposit = 50;
balance += deposit;  // Hoạt động, nhưng...
// Vấn đề: lỡ cộng USD với EUR? Compiler không bắt lỗi!
```

```csharp
// Cách DDD — dùng Money value object
var usd = new Money(100, "USD");
var eur = new Money(50, "EUR");
var result = usd + eur;  // THROW exception! Bắt lỗi logic ngay
```

Money value object **encode business rules vào type system** — compiler và runtime giúp bạn phát hiện lỗi sớm.

---

## 8. PagedList\<T\> — Kết quả phân trang

**File**: `src/shared/FinCore.SharedKernel/Pagination/PagedList.cs`

```csharp
public class PagedList<T>
{
    public IReadOnlyList<T> Items { get; }
    public int TotalCount { get; }
    public int PageNumber { get; }
    public int PageSize { get; }
    public int TotalPages => (int)Math.Ceiling(TotalCount / (double)PageSize);
    public bool HasPreviousPage => PageNumber > 1;
    public bool HasNextPage => PageNumber < TotalPages;

    public PagedList(IReadOnlyList<T> items, int totalCount, int pageNumber, int pageSize)
    {
        Items = items;
        TotalCount = totalCount;
        PageNumber = pageNumber;
        PageSize = pageSize;
    }
}
```

Dùng cho query trả về danh sách dài (VD: danh sách tài khoản của user). Thay vì trả về 10,000 accounts, trả về 20 accounts + metadata để client biết còn bao nhiêu trang.

---

## Tóm tắt

| Building Block | Vai trò | Đặc điểm chính |
|---|---|---|
| **AggregateRoot** | Gốc của aggregate, quản lý state và events | `RaiseEvent()` → `Apply()` → `Version++` |
| **ValueObject** | Giá trị immutable, so sánh bằng value | `GetEqualityComponents()`, sealed class |
| **Entity** | Đối tượng có identity (Id) | So sánh bằng `Id`, không phải value |
| **DomainEvent** | Ghi nhận sự kiện đã xảy ra | `record` type, immutable, auto-generated `EventId` |
| **DomainException** | Lỗi business rule | Được map thành HTTP 400 bởi middleware |
| **Result\<T\>** | Kết quả thành công/thất bại không dùng exception | `IsSuccess`, `Value`, `Error` |
| **Money** | Số tiền + loại tiền, operator overloading | Ngăn cộng khác loại tiền, immutable |

**Nguyên tắc xuyên suốt**: SharedKernel không phụ thuộc bất kỳ framework nào — chỉ C# thuần túy. Đây là nền tảng mà TẤT CẢ domain logic xây dựng trên.

> **Tiếp theo**: [Bài 2: Domain Layer](./02-domain-layer.md) — Xem cách Account và User aggregate sử dụng các building blocks này trong thực tế.
