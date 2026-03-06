# Bài 8: Observability — Logging, Tracing & Health Checks

> **Dành cho**: Junior Developer muốn hiểu cách monitor và debug ứng dụng production
> **Thời gian đọc**: ~15 phút
> **Prerequisite**: Đã đọc [Bài 5: API Layer](./05-api-layer.md)

## Bạn sẽ học được gì

- Structured logging là gì, tại sao tốt hơn `Console.WriteLine`
- Serilog: enrichers, sinks, log levels
- Correlation ID: tracking request xuyên suốt hệ thống
- OpenTelemetry: distributed tracing
- Health checks: liveness vs readiness
- Seq: UI để tìm kiếm và phân tích logs

---

## Structured Logging vs Traditional Logging

### Cách cũ — Console.WriteLine / string interpolation

```csharp
Console.WriteLine($"[{DateTime.Now}] User {userId} logged in from {ipAddress}");
// Output: [2026-03-06 14:30:22] User 5a4b3c1d logged in from 192.168.1.1
```

**Vấn đề**: Output là **plain text**. Muốn tìm "tất cả login của user X" → phải regex/grep → chậm, dễ sai.

### Cách mới — Structured Logging

```csharp
_logger.LogInformation("User {UserId} logged in from {IpAddress}", userId, ipAddress);
// Output JSON:
// {
//   "Timestamp": "2026-03-06T14:30:22Z",
//   "Level": "Information",
//   "Message": "User 5a4b3c1d logged in from 192.168.1.1",
//   "Properties": {
//     "UserId": "5a4b3c1d",
//     "IpAddress": "192.168.1.1",
//     "ServiceName": "identity",
//     "CorrelationId": "abc-123"
//   }
// }
```

**Lợi ích**: Mỗi log entry là **JSON object** với properties riêng biệt. Tìm `UserId = "5a4b3c1d"` → kết quả chính xác trong milliseconds.

---

## Serilog Setup

**File**: `src/shared/FinCore.Observability/Logging/SerilogExtensions.cs`

```csharp
public static IHostBuilder AddFinCoreLogging(this IHostBuilder hostBuilder, string serviceName)
{
    return hostBuilder.UseSerilog((context, configuration) =>
    {
        configuration
            .Enrich.WithProperty("ServiceName", serviceName)  // "identity" hoặc "accounts"
            .Enrich.WithMachineName()                         // Tên máy (debug multi-instance)
            .Enrich.WithThreadId()                            // Thread ID (debug concurrency)
            .WriteTo.Console()                                // Output ra terminal
            .WriteTo.Seq(seqUrl);                             // Gửi tới Seq server
    });
}
```

### Enrichers — Thêm context vào mỗi log

| Enricher | Giá trị | Mục đích |
|---|---|---|
| `ServiceName` | "identity" / "accounts" | Phân biệt log từ service nào |
| `MachineName` | "DEV-PC" | Phân biệt instance trong cluster |
| `ThreadId` | 14 | Debug concurrency issues |
| `CorrelationId` | "abc-123" | Tracking request (thêm bởi middleware) |

### Sinks — Log đi đâu?

| Sink | Đích | Mục đích |
|---|---|---|
| `Console` | Terminal | Dev xem realtime |
| `Seq` | HTTP → Seq server | Search, filter, analyze |

### Log Levels

```csharp
_logger.LogDebug("...");       // Chi tiết nhất, chỉ bật khi debug
_logger.LogInformation("..."); // Hoạt động bình thường
_logger.LogWarning("...");     // Có vấn đề nhưng chưa nghiêm trọng
_logger.LogError(ex, "...");   // Lỗi, cần xử lý
_logger.LogCritical("...");    // Hệ thống sắp sập
```

Trong FinCore:
- **Information**: "User registered", "Account opened"
- **Warning**: DomainException (business rule violation)
- **Error**: Unhandled exception (bug code)

---

## Correlation ID — Tracking request

### Vấn đề

Một request POST /api/v1/auth/login tạo ra ~10 log entries:
- Controller nhận request
- Validator chạy
- Handler bắt đầu
- DB query
- BCrypt verify
- JWT generate
- DB update
- Response trả về

Nếu 1000 requests/giây → 10,000 log entries → làm sao biết log nào thuộc request nào?

### Giải pháp: Correlation ID

```
Client gửi header: X-Correlation-Id: "abc-123"
  (hoặc server tự tạo Guid nếu không có header)

Mọi log entry có: CorrelationId = "abc-123"
Response header: X-Correlation-Id: "abc-123"
Error body: { correlationId: "abc-123" }
```

**Tìm kiếm trong Seq**: `CorrelationId = "abc-123"` → tất cả logs của request đó, theo thứ tự thời gian.

### CorrelationIdMiddleware

```csharp
public async Task InvokeAsync(HttpContext context)
{
    // 1. Lấy từ header hoặc tạo mới
    var correlationId = context.Request.Headers["X-Correlation-Id"].FirstOrDefault()
        ?? Guid.NewGuid().ToString();

    // 2. Lưu vào context (cho ExceptionHandlingMiddleware dùng)
    context.Items["CorrelationId"] = correlationId;

    // 3. Đẩy vào Serilog LogContext (mọi log tự có CorrelationId)
    using (LogContext.PushProperty("CorrelationId", correlationId))
    {
        // 4. Thêm vào response header (client nhận được)
        context.Response.Headers["X-Correlation-Id"] = correlationId;

        await _next(context);  // Tiếp tục pipeline
    }
}
```

### Microservices context

Khi Identity service gọi Accounts service (Phase 2+):
```
Client → Identity (CorrelationId: ABC)
  Identity logs: CorrelationId=ABC
  Identity → Accounts (header: X-Correlation-Id: ABC)
    Accounts logs: CorrelationId=ABC

→ Tìm ABC trong Seq = thấy logs từ CẢ HAI services!
```

---

## OpenTelemetry — Distributed Tracing

### Tracing là gì?

Logging ghi "chuyện gì xảy ra". Tracing ghi "mất bao lâu" và "gọi gì".

```
POST /api/v1/accounts/deposit
  ├─ Controller.Deposit        [2ms]
  ├─ MediatR.Send              [1ms]
  │   ├─ Validator             [0.5ms]
  │   └─ Handler               [45ms]
  │       ├─ DB.GetByIdAsync   [12ms]
  │       ├─ Deposit()         [0.1ms]
  │       └─ DB.UpdateAsync    [30ms]
  └─ Response                  [1ms]
  Total: 48ms
```

### Setup trong FinCore

```csharp
public static IServiceCollection AddFinCoreTracing(
    this IServiceCollection services, string serviceName)
{
    services.AddOpenTelemetry()
        .WithTracing(builder =>
        {
            builder
                .SetResourceBuilder(ResourceBuilder.CreateDefault()
                    .AddService(serviceName))
                .AddAspNetCoreInstrumentation()   // Auto-trace HTTP requests
                .AddHttpClientInstrumentation()   // Auto-trace outgoing HTTP
                .AddConsoleExporter();            // Phase 1: output ra console
        });                                       // Phase 2: export ra Jaeger

    return services;
}
```

**Phase 1**: Export ra console (dev debug).
**Phase 2**: Export ra Jaeger — UI để visualize traces, tìm bottleneck.

---

## Health Checks

### Liveness: "Ứng dụng có sống không?"

```
GET /health/live → 200 OK (nếu process running)
```

Kubernetes dùng: Nếu liên tục fail → **restart** container.

### Readiness: "Ứng dụng có sẵn sàng không?"

```
GET /health/ready → 200 OK (nếu DB kết nối được)
                  → 503 Service Unavailable (nếu DB down)
```

Kubernetes dùng: Nếu fail → **ngừng gửi traffic** đến instance này (nhưng không restart).

### Tại sao cần cả hai?

```
Scenario: DB restart (mất 30 giây)

Liveness:  200 ← app vẫn sống
Readiness: 503 ← nhưng chưa sẵn sàng nhận request

Kubernetes: Ngừng gửi traffic, nhưng KHÔNG restart container.
30 giây sau DB up: Readiness → 200 → Kubernetes gửi traffic lại.
```

Nếu chỉ có liveness: Kubernetes restart container vô ích → app start lại → DB vẫn down → restart nữa → **restart loop**.

---

## Seq — Log Aggregation UI

Seq chạy ở `http://localhost:8080` và nhận logs từ Serilog qua port 5341.

### Tại sao cần Seq?

- **Terminal** chỉ hiện logs realtime — scroll đi là mất
- **File logs** phải grep/tail — chậm, khó filter
- **Seq** cho phép:
  - Search: `ServiceName = "accounts" AND Level = "Error"`
  - Filter: `CorrelationId = "abc-123"`
  - Time range: logs trong 5 phút gần nhất
  - Dashboard: bao nhiêu errors/phút

### Cách dùng cơ bản

1. Mở `http://localhost:8080`
2. Search box: nhập query
3. Ví dụ queries:
   - `ServiceName = "identity"` — chỉ logs từ Identity service
   - `Level = "Error"` — chỉ errors
   - `@Message like "%deposit%"` — logs có chứa "deposit"
   - `CorrelationId = "abc-123"` — tất cả logs của một request

---

## Tóm tắt

1. **Structured logging** (Serilog) tốt hơn `Console.WriteLine` — searchable, filterable
2. **Enrichers** thêm context tự động: ServiceName, MachineName, ThreadId, CorrelationId
3. **Correlation ID** tracking request xuyên suốt pipeline và giữa services
4. **OpenTelemetry** trace thời gian thực thi — tìm bottleneck
5. **Health checks**: liveness (sống?) vs readiness (sẵn sàng?)
6. **Seq** UI tại `http://localhost:8080` — search, filter, analyze logs

> **Tiếp theo**: [Bài 9: Request Lifecycle](./09-request-lifecycle.md) — Walkthrough end-to-end từ HTTP request đến response.
