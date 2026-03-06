# Bài 7: Unit Testing — Test Domain Layer không cần Mock

> **Dành cho**: Junior Developer muốn hiểu cách viết và đọc unit tests trong DDD
> **Thời gian đọc**: ~20 phút
> **Prerequisite**: Đã đọc [Bài 2: Domain Layer](./02-domain-layer.md)

## Bạn sẽ học được gì

- Tại sao domain tests không cần mock
- xUnit basics: `[Fact]`, `[Theory]`, `[InlineData]`
- FluentAssertions: cách viết assertions dễ đọc
- Test patterns: Arrange → Act → Assert
- `ClearDomainEvents()` trong tests — tại sao cần
- Phân tích các tests thực tế trong FinCore

---

## Tại sao domain tests không cần mock?

Trong project thông thường, test service thường cần mock database, mock HTTP client, mock external services... Rất phiền.

**Trong DDD**, domain aggregates là **pure functions of events** — không phụ thuộc database hay framework. Test chỉ cần:

1. Tạo aggregate (gọi factory method)
2. Gọi domain method
3. Kiểm tra state và events

```csharp
// Không có mock! Không có database! Chỉ C# thuần túy.
var account = Account.Open(ownerId, AccountType.Checking, "USD");
account.Deposit(100m, "REF-001");
account.Balance.Amount.Should().Be(100m);
```

**Đây là lợi ích lớn nhất của DDD** — domain logic testable 100% mà không cần infrastructure.

---

## Framework Stack

| Framework | Vai trò |
|---|---|
| **xUnit** | Test runner — chạy tests |
| **FluentAssertions** | Assertions dễ đọc (`.Should().Be(...)`) |
| **Moq** | Mocking (không dùng trong Phase 1, chuẩn bị cho Phase 2) |
| **Bogus** | Fake data generation (chuẩn bị cho Phase 2) |
| **coverlet** | Code coverage |

---

## xUnit Basics

### [Fact] — Test đơn giản

```csharp
[Fact]
public void Deposit_PositiveAmount_IncreasesBalance()
{
    // Test body
}
```

`[Fact]` = một test case cụ thể. Nếu method throw exception → test fail.

### [Theory] + [InlineData] — Test với nhiều data

```csharp
[Theory]
[InlineData("")]
[InlineData(" ")]
[InlineData("invalid-email")]
[InlineData("missing@")]
[InlineData("@nodomain")]
public void Email_InvalidFormat_ThrowsDomainException(string invalidEmail)
{
    var act = () => new Email(invalidEmail);
    act.Should().Throw<DomainException>();
}
```

`[Theory]` = chạy test NHIỀU LẦN với data khác nhau. Mỗi `[InlineData]` là một lần chạy.

Ở đây, test chạy 5 lần — mỗi lần với một email invalid khác nhau. Tất cả đều phải throw `DomainException`.

---

## FluentAssertions — Assertions dễ đọc

### So sánh

```csharp
// Cách cũ (xUnit Assert)
Assert.Equal(100m, account.Balance.Amount);
Assert.True(account.Status == AccountStatus.Active);
Assert.Throws<DomainException>(() => account.Close());

// FluentAssertions — đọc như tiếng Anh
account.Balance.Amount.Should().Be(100m);
account.Status.Should().Be(AccountStatus.Active);
var act = () => account.Close();
act.Should().Throw<DomainException>();
```

FluentAssertions dễ đọc hơn nhiều, đặc biệt khi test fail — error message rõ ràng:

```
// xUnit: Assert.Equal failed. Expected: 100, Actual: 70
// FluentAssertions: Expected account.Balance.Amount to be 100m, but found 70m.
```

### Assertions phổ biến trong FinCore

```csharp
// Kiểm tra giá trị
value.Should().Be(expected);
value.Should().BeTrue();
value.Should().BeNull();

// Kiểm tra collection
account.DomainEvents.Should().HaveCount(1);
account.DomainEvents[0].Should().BeOfType<MoneyDeposited>();

// Kiểm tra exception
var act = () => account.Withdraw(1000m, "REF");
act.Should().Throw<InsufficientFundsException>();
```

---

## Test Pattern: Arrange → Act → Assert

Mọi test trong FinCore đều theo pattern AAA:

```csharp
[Fact]
public void Withdraw_SufficientFunds_DecreasesBalance()
{
    // ARRANGE — chuẩn bị data
    var account = Account.Open(Guid.NewGuid(), AccountType.Checking, "USD");
    account.Deposit(500m, "REF-001");
    account.ClearDomainEvents();  // ← Quan trọng! Giải thích bên dưới

    // ACT — thực hiện action cần test
    account.Withdraw(200m, "REF-002");

    // ASSERT — kiểm tra kết quả
    account.Balance.Amount.Should().Be(300m);
    account.DomainEvents.Should().HaveCount(1);
    account.DomainEvents[0].Should().BeOfType<MoneyWithdrawn>();
}
```

### Tại sao gọi ClearDomainEvents() trong Arrange?

Khi bạn gọi `Account.Open()`, nó raise `AccountOpened` event.
Khi bạn gọi `account.Deposit(500m, ...)`, nó raise `MoneyDeposited` event.

Nếu KHÔNG clear: `DomainEvents` sẽ có 3 events [AccountOpened, MoneyDeposited, MoneyWithdrawn].

Nhưng test chỉ muốn kiểm tra **Withdraw** raise đúng event → cần clear events trước khi Act → chỉ còn 1 event [MoneyWithdrawn].

---

## Phân tích tests thực tế

### AccountAggregateTests — 13 tests

**File**: `tests/unit/FinCore.Accounts.Domain.Tests/AccountAggregateTests.cs`

#### Test 1: Open account — kiểm tra event

```csharp
[Fact]
public void Open_WithValidData_RaisesAccountOpenedEvent()
{
    var account = Account.Open(OwnerId, AccountType.Checking, "USD");

    account.DomainEvents.Should().HaveCount(1);
    account.DomainEvents[0].Should().BeOfType<AccountOpened>();

    var @event = (AccountOpened)account.DomainEvents[0];
    @event.OwnerId.Should().Be(OwnerId);
    @event.Currency.Should().Be("USD");
}
```

Kiểm tra: gọi `Open()` → có đúng 1 event `AccountOpened` → event chứa đúng data.

#### Test 2: Open account — kiểm tra state

```csharp
[Fact]
public void Open_WithValidData_SetsInitialState()
{
    var account = Account.Open(OwnerId, AccountType.Checking, "USD");

    account.OwnerId.Should().Be(OwnerId);
    account.Status.Should().Be(AccountStatus.Active);
    account.Balance.Amount.Should().Be(0m);
}
```

Kiểm tra: sau `Open()`, state phải đúng — Active, Balance = 0.

#### Test 3: Rút tiền — insufficient funds

```csharp
[Fact]
public void Withdraw_InsufficientFunds_ThrowsInsufficientFundsException()
{
    var account = Account.Open(OwnerId, AccountType.Checking, "USD");
    account.Deposit(100m, "REF-001");

    var act = () => account.Withdraw(200m, "REF-002");

    act.Should().Throw<InsufficientFundsException>();
}
```

Deposit 100, rút 200 → throw InsufficientFundsException. Business rule "số dư phải đủ" được test.

#### Test 4: Đóng TK — non-zero balance

```csharp
[Fact]
public void Close_NonZeroBalance_ThrowsDomainException()
{
    var account = Account.Open(OwnerId, AccountType.Checking, "USD");
    account.Deposit(100m, "REF-001");

    var act = () => account.Close();

    act.Should().Throw<DomainException>()
        .WithMessage("*non-zero balance*");  // Wildcard matching
}
```

`WithMessage("*non-zero balance*")` — kiểm tra message chứa text "non-zero balance" (wildcard `*`).

### MoneyValueObjectTests — 6 tests

**File**: `tests/unit/FinCore.Accounts.Domain.Tests/MoneyValueObjectTests.cs`

#### Test: Cộng khác currency

```csharp
[Fact]
public void Add_DifferentCurrencies_ThrowsException()
{
    var usd = new Money(100, "USD");
    var eur = new Money(50, "EUR");

    var act = () => usd + eur;

    act.Should().Throw<InvalidOperationException>();
}
```

#### Test: Equality by value

```csharp
[Fact]
public void Money_EqualityByValue()
{
    var money1 = new Money(100, "USD");
    var money2 = new Money(100, "USD");

    money1.Should().Be(money2);      // Value equality
    (money1 == money2).Should().BeTrue();  // Operator ==
}
```

Hai Money object khác nhau (khác reference) nhưng **bằng nhau** vì cùng Amount + CurrencyCode.

### EmailValueObjectTests — 6 tests

#### Test: Case-insensitive

```csharp
[Fact]
public void Email_CaseInsensitiveEquality()
{
    var email1 = new Email("User@Example.COM");
    var email2 = new Email("user@example.com");

    email1.Should().Be(email2);  // Bằng nhau vì cả hai normalize thành lowercase
}
```

### UserAggregateTests — 10 tests

#### Test: Login inactive user

```csharp
[Fact]
public void Login_InactiveUser_ThrowsUserDeactivatedException()
{
    var user = User.Register(TestEmail, TestHash, UserRole.Customer);
    user.Deactivate();

    var act = () => user.Login();

    act.Should().Throw<UserDeactivatedException>();
}
```

Tạo user → deactivate → login → throw. Business rule: user bị deactivate không thể login.

---

## Cách chạy tests

```bash
# Chạy tất cả tests
dotnet test FinCore.slnx

# Chạy tests cho một project
dotnet test tests/unit/FinCore.Accounts.Domain.Tests/FinCore.Accounts.Domain.Tests.csproj

# Chạy một test class cụ thể
dotnet test tests/unit/FinCore.Accounts.Domain.Tests/FinCore.Accounts.Domain.Tests.csproj \
  --filter "FullyQualifiedName~AccountAggregateTests"

# Chạy một test method cụ thể
dotnet test tests/unit/FinCore.Accounts.Domain.Tests/FinCore.Accounts.Domain.Tests.csproj \
  --filter "FullyQualifiedName~Withdraw_InsufficientFunds"
```

---

## Tóm tắt

1. **Domain tests không cần mock** — aggregates là pure functions, chỉ C# thuần túy
2. **xUnit**: `[Fact]` cho test đơn, `[Theory]` + `[InlineData]` cho test nhiều data
3. **FluentAssertions**: `.Should().Be()`, `.Should().Throw<>()` — đọc như tiếng Anh
4. **Pattern AAA**: Arrange (chuẩn bị) → Act (thực hiện) → Assert (kiểm tra)
5. **`ClearDomainEvents()`** trước Act — chỉ kiểm tra events của action cần test
6. **Test business rules**: insufficient funds, inactive user, non-zero balance close...
7. **Test value objects**: equality, validation, normalization

> **Tiếp theo**: [Bài 8: Observability](./08-observability.md) — Structured logging, tracing, và cách debug production issues.
