# Bài 3: Application Layer — CQRS với MediatR

> **Dành cho**: Junior Developer muốn hiểu cách tổ chức workflow và tách biệt đọc/ghi
> **Thời gian đọc**: ~25 phút
> **Prerequisite**: Đã đọc [Bài 2: Domain Layer](./02-domain-layer.md)

## Bạn sẽ học được gì

- CQRS là gì và tại sao cần tách Command vs Query
- MediatR hoạt động ra sao — Mediator pattern
- Cấu trúc thư mục: một folder cho mỗi command/query
- Command flow chi tiết: từ request → handler → domain → result
- Query flow: tại sao query KHÔNG load aggregate
- FluentValidation: validate input trước khi xử lý
- DTOs: tại sao cần và cách thiết kế

---

## CQRS là gì?

**CQRS = Command Query Responsibility Segregation** — tách biệt trách nhiệm giữa **ghi** (Command) và **đọc** (Query).

### Cách "naive" (thường thấy)

```csharp
// Một service làm cả đọc và ghi
public class AccountService
{
    public void Deposit(Guid id, decimal amount) { /* load → sửa → save */ }
    public AccountDto GetById(Guid id) { /* load → map → return */ }
    public List<AccountDto> GetByOwner(Guid ownerId) { /* query → return */ }
}
```

Có gì sai? Khi system lớn lên:
- **Đọc** cần tối ưu performance (pagination, caching, projections)
- **Ghi** cần enforce business rules (validation, domain logic, events)
- Hai nhu cầu này **khác nhau hoàn toàn** — gom chung làm class phình to và khó maintain

### Cách CQRS

```
Commands (Ghi):                    Queries (Đọc):
┌─────────────────────┐           ┌──────────────────────┐
│ DepositCommand      │           │ GetAccountByIdQuery  │
│ → Load aggregate    │           │ → LINQ projection    │
│ → Call domain method│           │ → Return DTO directly│
│ → Save aggregate    │           │ → No aggregate load  │
│ → Return Result     │           │ → Return Result<DTO> │
└─────────────────────┘           └──────────────────────┘
        │                                   │
        ▼                                   ▼
  IAccountRepository              IAccountReadRepository
  (write repository)              (read repository)
```

**Command** load full aggregate, gọi domain method, save lại → đảm bảo business rules.
**Query** project trực tiếp từ database thành DTO → nhanh, nhẹ, không cần load toàn bộ entity.

---

## MediatR — Mediator Pattern

### Vấn đề không có MediatR

```csharp
// Controller phải biết handler nào để gọi
public class AccountsController
{
    private readonly DepositHandler _depositHandler;
    private readonly WithdrawHandler _withdrawHandler;
    private readonly OpenAccountHandler _openHandler;
    private readonly GetAccountByIdHandler _getByIdHandler;
    // ... 10 handler nữa

    public AccountsController(/* inject tất cả */) { }
}
```

Controller phải inject mọi handler — coupling rất chặt.

### Với MediatR

```csharp
public class AccountsController
{
    private readonly IMediator _mediator;  // Chỉ cần một dependency

    public async Task<IActionResult> Deposit(...)
    {
        var result = await _mediator.Send(new DepositCommand(...));
        // MediatR tự tìm handler phù hợp
    }
}
```

MediatR là **người trung gian** — controller gửi command/query, MediatR tự tìm và gọi handler tương ứng.

### Cách MediatR tìm handler

```
1. Controller gửi: _mediator.Send(new DepositCommand(...))
2. MediatR scan: "Ai implement IRequestHandler<DepositCommand, Result>?"
3. Tìm thấy: DepositCommandHandler
4. Gọi: handler.Handle(command, cancellationToken)
5. Trả kết quả về controller
```

**Convention**: Mỗi command/query có **đúng một handler** — mapping 1:1.

---

## Cấu trúc thư mục

```
Application/
├── Commands/
│   ├── OpenAccount/
│   │   ├── OpenAccountCommand.cs          # Record định nghĩa command
│   │   ├── OpenAccountCommandHandler.cs   # Logic xử lý
│   │   └── OpenAccountCommandValidator.cs # Validation rules
│   ├── Deposit/
│   │   ├── DepositCommand.cs
│   │   ├── DepositCommandHandler.cs
│   │   └── DepositCommandValidator.cs
│   └── ... (Withdraw, Freeze, Close)
│
├── Queries/
│   ├── GetAccountById/
│   │   ├── GetAccountByIdQuery.cs
│   │   └── GetAccountByIdQueryHandler.cs  # Query không cần validator
│   └── GetAccountsByOwner/
│       ├── GetAccountsByOwnerQuery.cs
│       └── GetAccountsByOwnerQueryHandler.cs
│
├── DTOs/
│   ├── AccountDto.cs
│   └── AccountSummaryDto.cs
│
└── Repositories/
    └── IAccountReadRepository.cs          # Interface cho read side
```

**Convention**: Mỗi command/query có **folder riêng** chứa 2-3 files. Dễ tìm, dễ navigate, không file nào quá lớn.

---

## Command Flow — Ví dụ: OpenAccount

### Bước 1: Command record

**File**: `Commands/OpenAccount/OpenAccountCommand.cs`

```csharp
public record OpenAccountCommand(
    Guid OwnerId,
    string AccountType,
    string Currency) : IRequest<Result<Guid>>;
```

- `record` — immutable, value equality, concise syntax.
- `IRequest<Result<Guid>>` — nói cho MediatR biết "command này trả về `Result<Guid>`".
- Parameters: OwnerId (ai sở hữu), AccountType ("Checking"/"Savings"), Currency ("USD"/"EUR").

### Bước 2: Validator

**File**: `Commands/OpenAccount/OpenAccountCommandValidator.cs`

```csharp
public class OpenAccountCommandValidator : AbstractValidator<OpenAccountCommand>
{
    public OpenAccountCommandValidator()
    {
        RuleFor(x => x.OwnerId)
            .NotEmpty()
            .WithMessage("Owner ID is required.");

        RuleFor(x => x.AccountType)
            .NotEmpty()
            .Must(t => Enum.TryParse<AccountType>(t, true, out _))
            .WithMessage("Invalid account type. Must be: Checking, Savings, or Investment.");

        RuleFor(x => x.Currency)
            .NotEmpty()
            .Length(3)
            .WithMessage("Currency must be a 3-letter ISO 4217 code.");
    }
}
```

Validator chạy **TRƯỚC** handler — nếu input invalid, handler không cần thực thi.

**`Must(t => Enum.TryParse<AccountType>(t, true, out _))`** — kiểm tra string có phải enum hợp lệ không. `true` = case-insensitive ("checking" = "Checking").

### Bước 3: Handler

**File**: `Commands/OpenAccount/OpenAccountCommandHandler.cs`

```csharp
public class OpenAccountCommandHandler
    : IRequestHandler<OpenAccountCommand, Result<Guid>>
{
    private readonly IAccountRepository _repository;
    private readonly IEventBus _eventBus;

    public OpenAccountCommandHandler(
        IAccountRepository repository, IEventBus eventBus)
    {
        _repository = repository;
        _eventBus = eventBus;
    }

    public async Task<Result<Guid>> Handle(
        OpenAccountCommand request, CancellationToken cancellationToken)
    {
        // 1. Parse enum
        if (!Enum.TryParse<AccountType>(request.AccountType, true, out var accountType))
            return Result.Failure<Guid>($"Invalid account type: {request.AccountType}");

        // 2. Gọi domain factory method
        var account = Account.Open(
            request.OwnerId, accountType, request.Currency.ToUpperInvariant());

        // 3. Persist
        await _repository.AddAsync(account, cancellationToken);

        // 4. Clear events (Phase 2 sẽ publish qua Kafka)
        account.ClearDomainEvents();

        // 5. Trả về ID tài khoản mới
        return Result.Success(account.Id);
    }
}
```

### Luồng thực thi chi tiết

```
1. Controller nhận POST /api/v1/accounts
2. Controller gọi _mediator.Send(new OpenAccountCommand(...))
3. MediatR chạy OpenAccountCommandValidator
   → Nếu fail: trả về validation error (không vào handler)
   → Nếu pass: tiếp tục
4. MediatR gọi OpenAccountCommandHandler.Handle()
5. Handler parse AccountType enum
6. Handler gọi Account.Open()
   → Account constructor tạo instance
   → Account.Open() gọi RaiseEvent(new AccountOpened(...))
   → RaiseEvent gọi Apply(event) → set state (Id, Balance=0, Status=Active...)
   → RaiseEvent tăng Version++
7. Handler gọi _repository.AddAsync(account)
   → EfAccountRepository map Account → AccountEntity
   → EF Core INSERT vào database
8. Handler gọi account.ClearDomainEvents()
9. Handler trả về Result.Success(account.Id)
10. Controller nhận result, trả về HTTP 201
```

---

## Command Flow — Ví dụ: Deposit

```csharp
public record DepositCommand(
    Guid AccountId, decimal Amount,
    string Currency, string Reference) : IRequest<Result>;
```

```csharp
public async Task<Result> Handle(
    DepositCommand request, CancellationToken cancellationToken)
{
    // 1. Load aggregate từ database
    var account = await _repository.GetByIdAsync(request.AccountId, cancellationToken);
    if (account is null)
        return Result.Failure($"Account '{request.AccountId}' not found.");

    // 2. Gọi domain method (business rules chạy ở đây)
    account.Deposit(request.Amount, request.Reference);

    // 3. Save lại
    await _repository.UpdateAsync(account, cancellationToken);
    account.ClearDomainEvents();

    return Result.Success();
}
```

**Pattern chung của mọi command handler:**
1. **Load** aggregate từ repository
2. **Call** domain method (business rules)
3. **Save** aggregate qua repository
4. **Clear** domain events
5. **Return** Result

**Lưu ý**: Nếu `account.Deposit()` throw `DomainException` (VD: account frozen), exception sẽ "bay" lên đến `ExceptionHandlingMiddleware` và được map thành HTTP 400. Handler không cần try-catch.

---

## Query Flow — Ví dụ: GetAccountById

### Khác biệt chính với Command

```csharp
public record GetAccountByIdQuery(Guid AccountId) : IRequest<Result<AccountDto>>;
```

```csharp
public class GetAccountByIdQueryHandler
    : IRequestHandler<GetAccountByIdQuery, Result<AccountDto>>
{
    private readonly IAccountReadRepository _readRepository;  // READ repository!

    public GetAccountByIdQueryHandler(IAccountReadRepository readRepository)
    {
        _readRepository = readRepository;
    }

    public async Task<Result<AccountDto>> Handle(
        GetAccountByIdQuery request, CancellationToken cancellationToken)
    {
        var account = await _readRepository.GetByIdAsync(
            request.AccountId, cancellationToken);

        if (account is null)
            return Result.Failure<AccountDto>(
                $"Account '{request.AccountId}' not found.");

        return Result.Success(account);
    }
}
```

### Điểm khác so với Command

| | Command | Query |
|---|---|---|
| **Repository** | `IAccountRepository` (write) | `IAccountReadRepository` (read) |
| **Load aggregate?** | Có — cần full object | KHÔNG — project trực tiếp |
| **Gọi domain method?** | Có | KHÔNG |
| **Validator?** | Có | Thường không cần |
| **Return type** | `Result` hoặc `Result<Guid>` | `Result<AccountDto>` |

**Tại sao query không load aggregate?** Vì:
- Load aggregate = load toàn bộ data + reconstitute via reflection = **chậm**
- Query chỉ cần một subset data → LINQ projection chỉ SELECT cột cần thiết = **nhanh**
- Aggregate có domain methods mà query không dùng → waste memory

---

## Query với Pagination — GetAccountsByOwner

```csharp
public record GetAccountsByOwnerQuery(
    Guid OwnerId,
    int PageNumber = 1,
    int PageSize = 20) : IRequest<Result<PagedList<AccountSummaryDto>>>;
```

Handler gọi `_readRepository.GetByOwnerAsync(ownerId, pageNumber, pageSize)` trả về `PagedList<AccountSummaryDto>`.

**PagedList** chứa:
- `Items` — danh sách accounts trang hiện tại
- `TotalCount` — tổng số accounts (VD: 150)
- `PageNumber`, `PageSize` — metadata cho client phân trang
- `TotalPages`, `HasNextPage`, `HasPreviousPage` — calculated properties

---

## DTOs — Data Transfer Objects

### AccountDto (chi tiết)

```csharp
public record AccountDto(
    Guid Id,
    Guid OwnerId,
    string AccountNumber,
    string AccountType,
    decimal Balance,
    string Currency,
    string Status,
    DateTimeOffset CreatedAt);
```

### AccountSummaryDto (tóm tắt)

```csharp
public record AccountSummaryDto(
    Guid Id,
    string AccountNumber,
    string AccountType,
    decimal Balance,
    string Currency,
    string Status);
```

### Tại sao cần hai DTO?

- **AccountDto** — dùng khi xem chi tiết một account. Chứa đầy đủ thông tin (OwnerId, CreatedAt).
- **AccountSummaryDto** — dùng trong danh sách. Bỏ bớt trường không cần (giảm payload khi trả về 20 accounts).

**Quy tắc**: DTO là `record` (immutable, value equality) với positional parameters — ngắn gọn, rõ ràng.

---

## Identity Application — Login Command (phức tạp hơn)

```csharp
public async Task<Result<AuthTokens>> Handle(
    LoginUserCommand request, CancellationToken cancellationToken)
{
    // 1. Tìm user bằng email
    var email = new Email(request.Email);
    var user = await _userRepository.GetByEmailAsync(email, cancellationToken);
    if (user is null)
        return Result.Failure<AuthTokens>("Invalid email or password.");

    // 2. Verify password (BCrypt — chạy ở Infrastructure)
    if (!_passwordHasher.Verify(request.Password, user.PasswordHash.Value))
        return Result.Failure<AuthTokens>("Invalid email or password.");

    // 3. Gọi domain method (kiểm tra IsActive)
    user.Login();  // Throws UserDeactivatedException nếu user bị deactivate

    // 4. Tạo access token (JWT)
    var accessToken = _jwtTokenService.GenerateAccessToken(user);

    // 5. Tạo refresh token (random bytes)
    var rawRefreshToken = _jwtTokenService.GenerateRefreshToken();

    // 6. Hash refresh token trước khi lưu DB
    var tokenHash = SHA256Hash(rawRefreshToken);

    // 7. Thêm refresh token vào user aggregate
    var expiresAt = DateTimeOffset.UtcNow.AddDays(_jwtSettings.Value.RefreshTokenExpiryDays);
    user.AddRefreshToken(tokenHash, expiresAt);

    // 8. Save
    await _userRepository.UpdateAsync(user, cancellationToken);
    user.ClearDomainEvents();

    // 9. Trả về tokens (raw refresh token, không phải hash!)
    return Result.Success(new AuthTokens(
        accessToken, rawRefreshToken, expiresAt));
}
```

**Điểm quan trọng**: Client nhận **raw** refresh token, nhưng DB lưu **SHA-256 hash**. Nếu database bị hack, attacker chỉ thấy hash — không thể sử dụng.

---

## Interface Definitions — Tại sao ở Application layer?

```
Domain:        IAccountRepository (được dùng trong Application handlers)
Application:   IAccountReadRepository, IPasswordHasher, IJwtTokenService
Infrastructure: EfAccountRepository, AccountReadRepository, BcryptPasswordHasher, JwtTokenService
```

**Quy tắc:**
- Interface cho **write repository** (`IAccountRepository`) define ở **Domain** — vì domain methods cần nó
- Interface cho **read repository** và **services** define ở **Application** — vì chỉ handler cần
- **Implementation** luôn ở **Infrastructure** — đó là layer duy nhất biết EF Core, BCrypt, JWT

---

## Tóm tắt

1. **CQRS** tách Command (ghi, cần aggregate) và Query (đọc, chỉ cần projection)
2. **MediatR** là mediator — controller gửi command/query, MediatR tự tìm handler
3. **Mỗi command/query** có folder riêng chứa: record + handler + validator (nếu cần)
4. **Command pattern**: Load aggregate → Call domain method → Save → Return Result
5. **Query pattern**: Project từ DB trực tiếp → Return DTO (không load aggregate)
6. **FluentValidation** chạy trước handler — input invalid = handler không chạy
7. **DTOs** là record immutable, có nhiều variants cho từng use case

> **Tiếp theo**: [Bài 4: Infrastructure Layer](./04-infrastructure-layer.md) — EF Core, aggregate reconstitution, và cách data được lưu vào database.
