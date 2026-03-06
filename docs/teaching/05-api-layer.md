# Bài 5: API Layer — Controllers, Middleware & Startup Pipeline

> **Dành cho**: Junior Developer muốn hiểu cách HTTP requests được tiếp nhận và xử lý
> **Thời gian đọc**: ~20 phút
> **Prerequisite**: Đã đọc [Bài 4: Infrastructure Layer](./04-infrastructure-layer.md)

## Bạn sẽ học được gì

- Program.cs — startup pipeline và thứ tự middleware
- Controller design: thin controllers, delegate to MediatR
- Request/Response models: inline records
- Result\<T\> → HTTP status code mapping
- ExceptionHandlingMiddleware: DomainException → 400
- CorrelationIdMiddleware: tracking requests
- Health checks: liveness vs readiness

---

## Program.cs — Startup Pipeline

**File**: `src/services/FinCore.Accounts/FinCore.Accounts.Api/Program.cs`

Program.cs là **điểm bắt đầu** của ứng dụng. Nó cấu hình tất cả services và middleware.

### Phần 1: Service Registration (trước `Build()`)

```csharp
var builder = WebApplication.CreateBuilder(args);

// 1. Logging (Serilog)
builder.Host.AddFinCoreLogging("accounts");

// 2. Tracing (OpenTelemetry)
builder.Services.AddFinCoreTracing("accounts");

// 3. MediatR — scan assembly chứa commands/queries
builder.Services.AddMediatR(cfg =>
    cfg.RegisterServicesFromAssembly(typeof(OpenAccountCommand).Assembly));

// 4. FluentValidation — scan cùng assembly cho validators
builder.Services.AddValidatorsFromAssembly(typeof(OpenAccountCommand).Assembly);

// 5. Infrastructure — DbContext, repositories
builder.Services.AddAccountsInfrastructure(builder.Configuration);

// 6. JWT Authentication
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options => { /* token validation params */ });

// 7. Authorization
builder.Services.AddAuthorization();

// 8. Health checks
builder.Services.AddFinCoreHealthChecks()
    .AddNpgSql(connectionString);

// 9. Event bus (NoOp cho Phase 1)
builder.Services.AddScoped<IEventBus, NoOpEventBus>();

// 10. Controllers + OpenAPI
builder.Services.AddControllers();
builder.Services.AddOpenApi();
```

**Thứ tự registration** không quan trọng lắm (DI container resolve tại runtime). Nhưng sắp xếp logic theo nhóm giúp dễ đọc.

### Phần 2: Middleware Pipeline (sau `Build()`)

```csharp
var app = builder.Build();

// 1. Correlation ID — phải ĐẦU TIÊN (tracking mọi request)
app.UseMiddleware<CorrelationIdMiddleware>();

// 2. Exception handling — bọc mọi thứ phía sau
app.UseMiddleware<ExceptionHandlingMiddleware>();

// 3. OpenAPI/Scalar (chỉ Development)
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

// 4. Authentication — decode JWT token
app.UseAuthentication();

// 5. Authorization — kiểm tra [Authorize] attribute
app.UseAuthorization();

// 6. Health checks
app.MapHealthChecks("/health/live", new() { Predicate = _ => false });
app.MapHealthChecks("/health/ready");

// 7. Controller routing
app.MapControllers();

app.Run();
```

### Thứ tự middleware CỰC KỲ QUAN TRỌNG

```
Request → CorrelationId → ExceptionHandling → Auth → Authorization → Controller
Response ← CorrelationId ← ExceptionHandling ← Auth ← Authorization ← Controller
```

Middleware chạy như **hành tây** — request đi vào từ ngoài, response đi ra ngược lại.

**Tại sao CorrelationId phải đầu tiên?** Vì nếu exception xảy ra ở bất kỳ đâu, error response cần có correlationId để tracing.

**Tại sao ExceptionHandling phải trước Auth?** Vì nếu authentication throw exception, ta cần catch và trả về response phù hợp.

---

## Controllers — Thin & Focused

### Nguyên tắc: Controller chỉ làm 3 việc

1. **Extract** data từ request (body, route, claims)
2. **Send** command/query qua MediatR
3. **Map** result thành HTTP response

Controller KHÔNG chứa business logic — đó là việc của Domain và Application.

### AccountsController

**File**: `src/services/FinCore.Accounts/FinCore.Accounts.Api/Controllers/AccountsController.cs`

```csharp
[ApiController]
[Route("api/v1/accounts")]
[Authorize]
public class AccountsController : ControllerBase
{
    private readonly IMediator _mediator;

    public AccountsController(IMediator mediator)
    {
        _mediator = mediator;
    }
```

**`[ApiController]`** — Tự động validate model, trả về 400 nếu model invalid.
**`[Route("api/v1/accounts")]`** — Base URL cho tất cả endpoints.
**`[Authorize]`** — Tất cả endpoints cần JWT token (trừ khi override bằng `[AllowAnonymous]`).

### Endpoint: Open Account

```csharp
[HttpPost]
public async Task<IActionResult> OpenAccount(
    [FromBody] OpenAccountRequest request, CancellationToken ct)
{
    // 1. Extract: Lấy OwnerId từ JWT claims
    var ownerId = GetCurrentUserId();
    if (ownerId is null) return Unauthorized();

    // 2. Send: Gửi command qua MediatR
    var result = await _mediator.Send(
        new OpenAccountCommand(ownerId.Value, request.AccountType, request.Currency), ct);

    // 3. Map: Result → HTTP response
    if (!result.IsSuccess)
        return BadRequest(new { error = result.Error });

    return CreatedAtAction(nameof(GetById),
        new { id = result.Value }, new { id = result.Value });
}
```

**`CreatedAtAction`** — Trả về HTTP 201 Created + header `Location: /api/v1/accounts/{id}`.

### Endpoint: Get Account (ABAC)

```csharp
[HttpGet("{id:guid}")]
public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
{
    var result = await _mediator.Send(new GetAccountByIdQuery(id), ct);
    if (!result.IsSuccess)
        return NotFound(new { error = result.Error });

    // ABAC: kiểm tra quyền truy cập
    var currentUserId = GetCurrentUserId();
    var isPrivileged = User.IsInRole("Admin") || User.IsInRole("Analyst");

    if (!isPrivileged && result.Value.OwnerId != currentUserId)
        return Forbid();  // 403 — không có quyền

    return Ok(result.Value);
}
```

**ABAC (Attribute-Based Access Control)**: Customer chỉ xem account của mình. Admin/Analyst xem được tất cả. Chi tiết ở [Bài 6: Security](./06-security.md).

### Endpoint: Freeze (Role-based)

```csharp
[HttpPost("{id:guid}/freeze")]
[Authorize(Roles = "ComplianceOfficer,Admin")]
public async Task<IActionResult> Freeze(
    Guid id, [FromBody] FreezeRequest request, CancellationToken ct)
{
    var result = await _mediator.Send(
        new FreezeAccountCommand(id, request.Reason), ct);

    if (!result.IsSuccess)
        return BadRequest(new { error = result.Error });

    return Ok();
}
```

**`[Authorize(Roles = "ComplianceOfficer,Admin")]`** — Chỉ 2 roles này mới được freeze account. Customer và Analyst bị từ chối (403).

### Helper: GetCurrentUserId()

```csharp
private Guid? GetCurrentUserId()
{
    var claim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
        ?? User.FindFirst("sub")?.Value;
    return Guid.TryParse(claim, out var id) ? id : null;
}
```

Extract user ID từ JWT token. JWT chứa claim `sub` (subject) = user ID. Tại sao check cả `ClaimTypes.NameIdentifier` và `"sub"`? Vì tùy configuration, ASP.NET có thể map claim names khác nhau.

### Request Models

```csharp
public record OpenAccountRequest(string AccountType, string Currency);
public record MoneyOperationRequest(decimal Amount, string Currency, string Reference);
public record FreezeRequest(string Reason);
```

Đây là **inline records** — đơn giản, immutable, chỉ chứa data cần nhận từ HTTP body.

---

## ExceptionHandlingMiddleware

**File**: `Middleware/ExceptionHandlingMiddleware.cs`

```csharp
public class ExceptionHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionHandlingMiddleware> _logger;

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);  // Chạy toàn bộ pipeline phía sau
        }
        catch (DomainException ex)
        {
            // Business rule violation → 400
            _logger.LogWarning(ex, "Domain exception: {Message}", ex.Message);
            await WriteErrorResponse(context, 400, ex.Message);
        }
        catch (Exception ex)
        {
            // Lỗi không mong đợi → 500
            _logger.LogError(ex, "Unhandled exception");
            await WriteErrorResponse(context, 500, "An unexpected error occurred.");
        }
    }

    private static async Task WriteErrorResponse(
        HttpContext context, int statusCode, string message)
    {
        var correlationId = context.Items["CorrelationId"]?.ToString();

        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/json";

        var body = JsonSerializer.Serialize(new
        {
            error = message,
            correlationId
        });

        await context.Response.WriteAsync(body);
    }
}
```

### Hai loại error

| Loại | Exception | HTTP | Log Level | Message cho client |
|---|---|---|---|---|
| Business | `DomainException` | 400 | Warning | Message gốc (VD: "Insufficient funds") |
| System | `Exception` | 500 | Error | Generic "An unexpected error occurred." |

**Tại sao 500 không trả message gốc?** Vì lỗi hệ thống có thể chứa thông tin nhạy cảm (stack trace, connection string...). Client chỉ thấy generic message + correlationId để support team tìm log.

---

## CorrelationIdMiddleware

**File**: `src/shared/FinCore.Observability/Middleware/CorrelationIdMiddleware.cs`

```
Request đến:
  Header X-Correlation-Id: "abc-123"  → dùng "abc-123"
  Không có header                      → tạo Guid mới

Middleware:
  1. Lấy/tạo correlationId
  2. Đặt vào context.Items["CorrelationId"]
  3. Đặt vào Serilog LogContext
  4. Thêm vào Response header

Request đi tiếp → tất cả logs có correlationId
Response trả về → client nhận được correlationId
```

**Tại sao cần?** Khi debug production issue:
1. Client report lỗi: "Tôi nhận được correlationId XYZ"
2. Dev tìm trong Seq: `CorrelationId = "XYZ"`
3. Thấy TẤT CẢ logs liên quan đến request đó — từ controller → handler → database

---

## Health Checks

```csharp
app.MapHealthChecks("/health/live", new() { Predicate = _ => false });
app.MapHealthChecks("/health/ready");
```

### Liveness vs Readiness

**`/health/live`** — "Ứng dụng có đang chạy không?"
- `Predicate = _ => false` nghĩa là **không check gì cả** — luôn trả về 200 nếu process alive.
- Dùng bởi Kubernetes để quyết định có cần **restart** container không.

**`/health/ready`** — "Ứng dụng có sẵn sàng nhận request không?"
- Check PostgreSQL connection (via `AddNpgSql(...)`)
- Nếu DB down → trả về 503 → Kubernetes **ngừng gửi traffic** đến instance này.

---

## Tóm tắt

1. **Program.cs** chia 2 phần: Service Registration (DI) và Middleware Pipeline (thứ tự quan trọng!)
2. **Controllers** chỉ Extract → Send → Map — không chứa business logic
3. **ExceptionHandlingMiddleware** catch DomainException (400) và Exception (500), kèm correlationId
4. **CorrelationIdMiddleware** tracking request xuyên suốt pipeline — quan trọng cho debugging
5. **Health checks**: `/health/live` (liveness, luôn 200), `/health/ready` (readiness, check DB)
6. **Request models** là inline records — đơn giản, immutable
7. **ABAC** kiểm tra ở controller: user chỉ truy cập resource của mình

> **Tiếp theo**: [Bài 6: Security](./06-security.md) — JWT authentication, password hashing, refresh token rotation chi tiết.
