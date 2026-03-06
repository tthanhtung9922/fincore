# Bài 9: Request Lifecycle — Từ HTTP Request đến Response

> **Dành cho**: Junior Developer muốn hiểu toàn bộ luồng xử lý end-to-end
> **Thời gian đọc**: ~25 phút
> **Prerequisite**: Đã đọc tất cả bài trước (01-08)

## Bạn sẽ học được gì

- Toàn bộ lifecycle của một HTTP request qua mọi layer
- Ví dụ 1: POST /api/v1/accounts/{id}/deposit (happy path)
- Ví dụ 2: POST /api/v1/auth/login (authentication flow)
- Error flows: validation fail, domain exception, unhandled exception
- Tổng kết kiến thức từ tất cả bài trước

---

## Ví dụ 1: Deposit — Nạp tiền vào tài khoản

### HTTP Request

```
POST /api/v1/accounts/5a4b3c1d-e6f7-8901-abcd-ef1234567890/deposit
Headers:
  Authorization: Bearer eyJhbGciOiJIUzI1NiIs...
  X-Correlation-Id: req-abc-123
  Content-Type: application/json
Body:
  { "amount": 500.00, "currency": "USD", "reference": "DEPOSIT-001" }
```

### Step-by-Step Flow

```
                          ┌─────────────────────────────────┐
                          │         HTTP Request             │
                          └──────────────┬──────────────────┘
                                         │
                          ┌──────────────▼──────────────────┐
                     ① │   CorrelationIdMiddleware         │
                          │   → Đọc header X-Correlation-Id  │
                          │   → Set "req-abc-123" vào context│
                          │   → Push vào Serilog LogContext   │
                          └──────────────┬──────────────────┘
                                         │
                          ┌──────────────▼──────────────────┐
                     ② │   ExceptionHandlingMiddleware     │
                          │   → Try { await _next() }        │
                          │   → Catch exceptions phía dưới   │
                          └──────────────┬──────────────────┘
                                         │
                          ┌──────────────▼──────────────────┐
                     ③ │   JWT Authentication Middleware   │
                          │   → Đọc "Authorization: Bearer"  │
                          │   → Decode JWT, verify signature  │
                          │   → Populate HttpContext.User     │
                          │   → Claims: sub=userId, role=...  │
                          └──────────────┬──────────────────┘
                                         │
                          ┌──────────────▼──────────────────┐
                     ④ │   Authorization Middleware        │
                          │   → Check [Authorize] attribute   │
                          │   → User authenticated? → OK      │
                          └──────────────┬──────────────────┘
                                         │
                          ┌──────────────▼──────────────────┐
                     ⑤ │   AccountsController.Deposit()   │
                          │   → Extract accountId from route  │
                          │   → Extract request body          │
                          │   → _mediator.Send(DepositCommand)│
                          └──────────────┬──────────────────┘
                                         │
                          ┌──────────────▼──────────────────┐
                     ⑥ │   MediatR Pipeline                │
                          │   → Tìm DepositCommandHandler     │
                          │   → (FluentValidation nếu có)     │
                          │   → Gọi handler.Handle()          │
                          └──────────────┬──────────────────┘
                                         │
          ┌──────────────────────────────▼──────────────────────────────┐
     ⑦ │                  DepositCommandHandler                      │
          │                                                              │
          │   // 7a. Load aggregate từ database                          │
          │   var account = await _repository.GetByIdAsync(accountId);   │
          │                                                              │
          │        ┌────────────────────────────────────────┐            │
          │        │  EfAccountRepository.GetByIdAsync()    │            │
          │        │  → EF Core: SELECT * FROM accounts     │            │
          │        │    WHERE id = @id                      │            │
          │        │  → MapToDomain():                      │            │
          │        │    - GetUninitializedObject(Account)   │            │
          │        │    - Init _domainEvents via reflection │            │
          │        │    - Set all properties via reflection │            │
          │        │  → Return Account aggregate            │            │
          │        └────────────────────────────────────────┘            │
          │                                                              │
          │   // 7b. Gọi domain method                                   │
          │   account.Deposit(500m, "DEPOSIT-001");                      │
          │                                                              │
          │        ┌────────────────────────────────────────┐            │
          │        │  Account.Deposit()                     │            │
          │        │  → Check: Status == Active? ✓          │            │
          │        │  → Check: amount > 0? ✓                │            │
          │        │  → RaiseEvent(new MoneyDeposited(...)) │            │
          │        │    → _domainEvents.Add(event)          │            │
          │        │    → Apply(event):                     │            │
          │        │      Balance = 0 + Money(500, "USD")   │            │
          │        │      Balance = Money(500, "USD")       │            │
          │        │    → Version++ (1 → 2)                 │            │
          │        └────────────────────────────────────────┘            │
          │                                                              │
          │   // 7c. Save aggregate                                      │
          │   await _repository.UpdateAsync(account);                    │
          │                                                              │
          │        ┌────────────────────────────────────────┐            │
          │        │  EfAccountRepository.UpdateAsync()     │            │
          │        │  → Load entity by Id                   │            │
          │        │  → Set BalanceAmount = 500.00           │            │
          │        │  → Set Version = 2                      │            │
          │        │  → EF Core: UPDATE accounts SET ...    │            │
          │        │    WHERE id = @id AND version = 1      │            │
          │        │    (optimistic concurrency check)       │            │
          │        └────────────────────────────────────────┘            │
          │                                                              │
          │   // 7d. Clear events                                        │
          │   account.ClearDomainEvents();                               │
          │                                                              │
          │   // 7e. Return                                              │
          │   return Result.Success();                                   │
          └──────────────────────────────────────────────────────────────┘
                                         │
                          ┌──────────────▼──────────────────┐
                     ⑧ │   Controller nhận Result          │
                          │   → result.IsSuccess == true      │
                          │   → return Ok()                   │
                          └──────────────┬──────────────────┘
                                         │
                          ┌──────────────▼──────────────────┐
                          │       HTTP Response              │
                          │   Status: 200 OK                 │
                          │   Header: X-Correlation-Id:      │
                          │           req-abc-123             │
                          └─────────────────────────────────┘
```

---

## Ví dụ 2: Login — Flow phức tạp hơn

### HTTP Request

```
POST /api/v1/auth/login
Body: { "email": "john@example.com", "password": "MySecureP@ss123" }
```

### Step-by-Step (chỉ phần handler vì middleware giống nhau)

```
LoginUserCommandHandler.Handle()
│
├─ 1. Tạo Email value object
│     new Email("john@example.com")
│     → Validate format ✓
│     → Normalize: "john@example.com" (đã lowercase)
│
├─ 2. Tìm user bằng email
│     _userRepository.GetByEmailAsync(email)
│     → SQL: SELECT * FROM users WHERE email = 'john@example.com'
│     → MapToDomain(): User.Register() + reflection override
│     → Include refresh tokens
│     → Tìm thấy user (ID: aaa-bbb)
│
├─ 3. Verify password
│     _passwordHasher.Verify("MySecureP@ss123", user.PasswordHash.Value)
│     → BCrypt.Verify() so sánh plaintext với hash
│     → Kết quả: true ✓
│
├─ 4. Gọi domain method
│     user.Login()
│     → Check: IsActive == true? ✓
│     → RaiseEvent(new UserLoggedIn(userId, now))
│
├─ 5. Tạo Access Token (JWT)
│     _jwtTokenService.GenerateAccessToken(user)
│     → Claims: sub=aaa-bbb, email=john@..., role=Customer, jti=new-guid
│     → Sign: HS256 with JWT__SECRET
│     → Expires: now + 15 minutes
│     → Return: "eyJhbGciOiJIUzI1NiIs..."
│
├─ 6. Tạo Refresh Token
│     _jwtTokenService.GenerateRefreshToken()
│     → 64 random bytes → Base64
│     → Return: "eE7p9K2mN...xYzA==" (raw token)
│
├─ 7. Hash refresh token cho DB
│     SHA256.HashData(Encoding.UTF8.GetBytes(rawRefreshToken))
│     → "$sha256$a1b2c3d4..."
│
├─ 8. Thêm refresh token vào user
│     user.AddRefreshToken(tokenHash, expiresAt: now + 7 days)
│     → new RefreshToken(hash, expiresAt)
│     → _refreshTokens.Add(token)
│
├─ 9. Save user (với refresh token mới)
│     _userRepository.UpdateAsync(user)
│     → SQL: INSERT INTO refresh_tokens (...)
│     → SQL: UPDATE users SET version = ...
│
├─ 10. Clear events
│      user.ClearDomainEvents()
│
└─ 11. Return
       Result.Success(new AuthTokens(
           accessToken,        // JWT string
           rawRefreshToken,    // Base64 string (client giữ cái này)
           expiresAt           // Khi nào access token hết hạn
       ))
```

### HTTP Response

```
Status: 200 OK
Body:
{
  "accessToken": "eyJhbGciOiJIUzI1NiIs...",
  "refreshToken": "eE7p9K2mN...xYzA==",
  "expiresAt": "2026-03-06T15:00:00Z"
}
```

---

## Error Flows

### Error 1: Validation Fail (FluentValidation)

```
POST /api/v1/accounts
Body: { "accountType": "InvalidType", "currency": "XXXXXX" }

Flow:
  Controller → MediatR → Validator → FAIL!
  → "Invalid account type" hoặc "Currency must be 3 chars"
  → Response: 400 Bad Request { "errors": [...] }

⚠️ Handler KHÔNG chạy — validator chặn trước
```

### Error 2: Domain Exception (Business Rule)

```
POST /api/v1/accounts/{id}/withdraw
Body: { "amount": 10000, "currency": "USD", "reference": "REF" }
(Account chỉ có $100)

Flow:
  Controller → MediatR → Handler
    → _repository.GetByIdAsync() → Account (Balance: $100)
    → account.Withdraw(10000, "REF")
    → THROW InsufficientFundsException! ← Domain rule violated

  Exception bay lên:
    Handler → MediatR → Controller → ExceptionHandlingMiddleware
    → catch (DomainException ex)
    → Log: Warning "Domain exception: Insufficient funds..."
    → Response: 400 { "error": "Insufficient funds...", "correlationId": "..." }
```

### Error 3: Unhandled Exception (Bug)

```
Kịch bản: Database connection timeout

Flow:
  Handler → _repository.GetByIdAsync()
    → EF Core → PostgreSQL → TIMEOUT!
    → THROW NpgsqlException

  Exception bay lên:
    ExceptionHandlingMiddleware
    → catch (Exception ex)
    → Log: Error "Unhandled exception" + full stack trace
    → Response: 500 { "error": "An unexpected error occurred.", "correlationId": "..." }

⚠️ KHÔNG trả về "NpgsqlException: connection timeout" cho client
   (thông tin nhạy cảm — attacker biết dùng PostgreSQL)
```

### Tổng hợp Error Handling

```
Request vào
  │
  ├─ JWT invalid?          → 401 Unauthorized (Auth middleware)
  ├─ Không có quyền?       → 403 Forbidden (Controller ABAC check)
  ├─ Validation fail?      → 400 Bad Request (FluentValidation)
  ├─ Domain rule violated? → 400 Bad Request (ExceptionHandlingMiddleware)
  ├─ Resource not found?   → 404 Not Found (Controller/Handler)
  └─ Bug/Infrastructure?   → 500 Internal Server Error (ExceptionHandlingMiddleware)
```

---

## Sequence Diagram — Deposit (tổng hợp)

```
Client          Controller      MediatR        Handler        Repository      Database
  │                │               │              │               │              │
  │  POST /deposit │               │              │               │              │
  │───────────────►│               │              │               │              │
  │                │  Send(cmd)    │              │               │              │
  │                │──────────────►│              │               │              │
  │                │               │  Handle(cmd) │               │              │
  │                │               │─────────────►│               │              │
  │                │               │              │  GetByIdAsync │              │
  │                │               │              │──────────────►│  SELECT ...  │
  │                │               │              │               │─────────────►│
  │                │               │              │               │◄─────────────│
  │                │               │              │◄──────────────│  Account     │
  │                │               │              │               │              │
  │                │               │              │ account.Deposit(500)         │
  │                │               │              │──┐ RaiseEvent                │
  │                │               │              │  │ Apply(event)              │
  │                │               │              │◄─┘ Balance += 500           │
  │                │               │              │               │              │
  │                │               │              │  UpdateAsync  │              │
  │                │               │              │──────────────►│  UPDATE ...  │
  │                │               │              │               │─────────────►│
  │                │               │              │               │◄─────────────│
  │                │               │              │◄──────────────│              │
  │                │               │              │               │              │
  │                │               │ Result.Ok    │               │              │
  │                │               │◄─────────────│               │              │
  │                │  Result.Ok    │              │               │              │
  │                │◄──────────────│              │               │              │
  │   200 OK       │              │               │              │              │
  │◄───────────────│               │              │               │              │
```

---

## Tổng kết — Map kiến thức qua các bài

```
HTTP Request đến
  │
  ├─ Bài 8: CorrelationIdMiddleware → tracking
  ├─ Bài 5: ExceptionHandlingMiddleware → error handling
  ├─ Bài 6: JWT Authentication → ai đang gọi?
  ├─ Bài 6: Authorization → có quyền không?
  │
  ├─ Bài 5: Controller → Extract, Send, Map
  │
  ├─ Bài 3: MediatR → tìm handler
  ├─ Bài 3: FluentValidation → validate input
  │
  ├─ Bài 3: Handler → điều phối workflow
  │   ├─ Bài 4: Repository.GetByIdAsync → load từ DB
  │   │   └─ Bài 4: Reconstitution → reflection magic
  │   ├─ Bài 2: Domain method → business rules + events
  │   │   └─ Bài 1: AggregateRoot.RaiseEvent → Apply → Version++
  │   └─ Bài 4: Repository.UpdateAsync → save vào DB
  │
  └─ Bài 5: Controller → Result → HTTP Response
```

Mỗi bài bạn đã đọc covers một phần của lifecycle. Bài này nối tất cả lại thành bức tranh hoàn chỉnh.

---

## Tóm tắt

1. Một request đi qua **6 stages**: Middleware → Auth → Controller → MediatR → Handler → Domain
2. **Happy path**: middleware → controller → handler → domain method → save → response
3. **Error paths**: mỗi layer có cách handle error riêng (401, 403, 400, 404, 500)
4. **Domain exceptions** bay lên ExceptionHandlingMiddleware → 400 với message rõ ràng
5. **Unhandled exceptions** → 500 với message generic (không lộ thông tin nhạy cảm)
6. **CorrelationId** theo request xuyên suốt — debug bất kỳ issue nào

---

## Kết thúc bộ tài liệu

Chúc mừng bạn đã đọc xong toàn bộ 10 bài! Bây giờ bạn đã hiểu:

- **Bài 0**: Tổng quan project, kiến trúc, cách chạy
- **Bài 1**: SharedKernel — building blocks (AggregateRoot, ValueObject, Result, Money)
- **Bài 2**: Domain Layer — aggregates, business rules, events, Apply pattern
- **Bài 3**: Application Layer — CQRS, MediatR, commands, queries, validators
- **Bài 4**: Infrastructure Layer — EF Core, repositories, aggregate reconstitution
- **Bài 5**: API Layer — controllers, middleware, startup pipeline
- **Bài 6**: Security — JWT, BCrypt, refresh tokens, ABAC
- **Bài 7**: Testing — unit tests không cần mock, FluentAssertions
- **Bài 8**: Observability — structured logging, correlation ID, health checks
- **Bài 9**: Request Lifecycle — end-to-end flow, error handling

**Bước tiếp theo**: Mở code, đặt breakpoint, chạy một request và trace qua từng layer. Không gì thay thế được trải nghiệm thực hành!
