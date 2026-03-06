# Bài 6: Security — JWT, Password Hashing & Access Control

> **Dành cho**: Junior Developer muốn hiểu cách authentication và authorization hoạt động
> **Thời gian đọc**: ~25 phút
> **Prerequisite**: Đã đọc [Bài 5: API Layer](./05-api-layer.md)

## Bạn sẽ học được gì

- JWT authentication: cấu trúc token, signing, validation
- Password hashing: BCrypt, workFactor, tại sao không hash trong Domain
- Refresh token rotation: flow đầy đủ, SHA-256, token chain
- ABAC: Attribute-Based Access Control
- Role-based authorization
- Security conventions trong FinCore

---

## JWT Authentication — Cách hoạt động

### JWT là gì?

**JWT = JSON Web Token** — một chuỗi string chứa thông tin user, được **ký số** (signed) để đảm bảo không bị giả mạo.

```
eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.
eyJzdWIiOiI1YTRiM2MxZC4uLiIsImVtYWlsIjoiam9obkBleGFtcGxlLmNvbSJ9.
SflKxwRJSMeKKF2QT4fwpMeJf36POk6yJV_adQssw5c
```

Ba phần ngăn cách bởi dấu `.`:
1. **Header** — thuật toán ký (HS256)
2. **Payload** — claims (thông tin user)
3. **Signature** — chữ ký, đảm bảo token không bị sửa

### Claims trong FinCore

```json
{
  "sub": "5a4b3c1d-...",    // User ID (subject)
  "email": "john@example.com",
  "role": "Customer",        // UserRole
  "jti": "a1b2c3d4-...",    // JWT ID (unique, chống replay)
  "exp": 1709744400,         // Expires at (15 phút)
  "iss": "fincore-identity", // Issuer
  "aud": "fincore"           // Audience
}
```

### Signing: HS256

```csharp
// JwtTokenService.cs — tạo token
var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret));
var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

var token = new JwtSecurityToken(
    issuer: _settings.Issuer,
    audience: _settings.Audience,
    claims: claims,
    expires: DateTime.UtcNow.AddMinutes(_settings.AccessTokenExpiryMinutes),
    signingCredentials: credentials);
```

**HS256 = HMAC-SHA256** — symmetric signing. Cùng một secret key dùng để **ký** (Identity service) và **verify** (Accounts service). Hai service phải share cùng `JWT__SECRET`.

### Validation ở Accounts service

```csharp
// Program.cs — Accounts service
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,           // Issuer phải = "fincore-identity"
            ValidateAudience = true,         // Audience phải = "fincore"
            ValidateLifetime = true,         // Token chưa hết hạn
            ValidateIssuerSigningKey = true, // Signature hợp lệ
            ValidIssuer = jwtIssuer,
            ValidAudience = jwtAudience,
            IssuerSigningKey = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(jwtSecret))
        };
    });
```

Mỗi request đến Accounts service:
1. Middleware đọc header `Authorization: Bearer <token>`
2. Decode JWT, verify signature bằng `JWT__SECRET`
3. Check issuer, audience, expiry
4. Nếu OK → populate `HttpContext.User` với claims
5. Nếu FAIL → trả về 401 Unauthorized

---

## Password Hashing — BCrypt

### Tại sao hash password?

Nếu lưu password plaintext ("MyP@ssw0rd") → database bị hack → attacker có password của TẤT CẢ users.

**Hash** biến password thành chuỗi **không thể đảo ngược**:
```
"MyP@ssw0rd" → "$2a$12$LJ3m4ys5Rn...KjH8qF2u"
```

Khi login, hash password user nhập và **so sánh hash**, không so sánh plaintext.

### BCrypt — Tại sao không dùng SHA-256?

SHA-256 quá **nhanh** (~1 triệu hash/giây). Attacker brute-force dễ dàng.

**BCrypt** có **workFactor** — điều chỉnh độ chậm:

```csharp
// BcryptPasswordHasher.cs
public string Hash(string plaintext)
    => BCrypt.Net.BCrypt.HashPassword(plaintext, workFactor: 12);

public bool Verify(string plaintext, string hash)
    => BCrypt.Net.BCrypt.Verify(plaintext, hash);
```

WorkFactor = 12 → ~100ms per hash. Nếu attacker thử 10 triệu password → cần ~12 ngày. SHA-256 chỉ cần ~10 giây.

### Tại sao hash KHÔNG nằm trong Domain?

```
Domain:         HashedPassword (chỉ là wrapper cho string)
Infrastructure: BcryptPasswordHasher (implement IPasswordHasher)
```

**Lý do:**
- BCrypt là library bên ngoài → Domain không phụ thuộc library
- Nếu đổi từ BCrypt sang Argon2 → chỉ sửa Infrastructure
- Domain chỉ biết "có một chuỗi hash" — không cần biết hash bằng gì

---

## Refresh Token Rotation

### Vấn đề: Access token hết hạn (15 phút)

User đang dùng app, access token hết hạn → bắt user đăng nhập lại? Trải nghiệm tệ!

### Giải pháp: Refresh token

```
Lần đăng nhập đầu:
  Client nhận: Access Token (15 phút) + Refresh Token (7 ngày)

Khi Access Token hết hạn:
  Client gửi Refresh Token → Server trả về Access Token MỚI + Refresh Token MỚI
  (Refresh Token cũ bị revoke)
```

### Flow chi tiết

```
1. POST /api/v1/auth/login
   Body: { email, password }

   Server:
   a. Verify password (BCrypt)
   b. Tạo Access Token (JWT, 15 phút)
   c. Tạo Refresh Token (64 random bytes → Base64)
   d. Hash Refresh Token bằng SHA-256
   e. Lưu HASH vào database (không phải raw token!)
   f. Trả về { accessToken, refreshToken (raw), expiresAt }

2. Client lưu tokens (localStorage, secure cookie, etc.)

3. Mỗi API call: Header "Authorization: Bearer <accessToken>"

4. Access Token hết hạn (15 phút sau):

5. POST /api/v1/auth/refresh
   Body: { userId, refreshToken (raw) }

   Server:
   a. Hash refreshToken bằng SHA-256
   b. Tìm trong DB: token có hash = X, chưa revoke, chưa hết hạn?
   c. Nếu tìm thấy:
      - Revoke token cũ (IsRevoked = true, ReplacedByToken = hash mới)
      - Tạo Access Token MỚI
      - Tạo Refresh Token MỚI
      - Lưu hash token mới vào DB
      - Trả về tokens mới
   d. Nếu KHÔNG tìm thấy: 401 Unauthorized

6. POST /api/v1/auth/revoke (logout)
   Body: { refreshToken }

   Server: Revoke token, không tạo token thay thế
```

### Tại sao lưu SHA-256 hash thay vì raw token?

```
Database lưu: "$sha256$a1b2c3d4..."  (hash)
Client giữ:   "eE7p9K2mN...xYzA=="    (raw)
```

Nếu database bị hack:
- Attacker thấy **hash** → không thể dùng (SHA-256 one-way)
- Raw token chỉ tồn tại **ở client** — server không lưu

### Token Rotation — Tại sao revoke token cũ?

```
Token A → sử dụng để refresh → Token B (Token A bị revoke)
Token B → sử dụng để refresh → Token C (Token B bị revoke)
```

Nếu attacker đánh cắp Token A và cố sử dụng:
- Token A đã bị revoke → **từ chối** → server biết có ai đó đang dùng token bị đánh cắp

`ReplacedByToken` field giúp tracing: Token A → bị thay bởi Token B → Token B bị thay bởi Token C. Tạo thành chain để audit.

---

## Access Control — Ai được làm gì?

### Role-Based Access Control (RBAC)

```csharp
// Chỉ ComplianceOfficer và Admin mới được freeze account
[Authorize(Roles = "ComplianceOfficer,Admin")]
[HttpPost("{id:guid}/freeze")]
public async Task<IActionResult> Freeze(...) { }
```

4 roles trong FinCore:

| Role | Quyền |
|---|---|
| **Customer** | CRUD tài khoản của mình, nạp/rút tiền |
| **Analyst** | Xem tất cả tài khoản (read-only) |
| **ComplianceOfficer** | Freeze/Unfreeze tài khoản |
| **Admin** | Tất cả quyền |

### Attribute-Based Access Control (ABAC)

RBAC không đủ: Customer có quyền xem account, nhưng chỉ account **của mình**. Đây là ABAC — quyết định dựa trên **thuộc tính** (OwnerId == currentUserId).

```csharp
[HttpGet("{id:guid}")]
public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
{
    var result = await _mediator.Send(new GetAccountByIdQuery(id), ct);
    if (!result.IsSuccess) return NotFound(...);

    var currentUserId = GetCurrentUserId();
    var isPrivileged = User.IsInRole("Admin") || User.IsInRole("Analyst");

    // ABAC check
    if (!isPrivileged && result.Value.OwnerId != currentUserId)
        return Forbid();  // 403

    return Ok(result.Value);
}
```

**Flow quyết định:**
```
Request: GET /api/v1/accounts/{id}
  │
  ├─ User là Admin/Analyst? → ✅ Cho xem
  │
  └─ User là Customer?
      ├─ Account.OwnerId == User.Id? → ✅ Cho xem
      └─ Account.OwnerId != User.Id? → ❌ 403 Forbidden
```

### 401 vs 403

| Code | Nghĩa | Ví dụ |
|---|---|---|
| **401 Unauthorized** | "Tôi không biết bạn là ai" | Không có JWT token, token hết hạn |
| **403 Forbidden** | "Tôi biết bạn là ai, nhưng bạn không có quyền" | Customer cố xem account người khác |

---

## Security Conventions

### 1. Không có secrets trong appsettings.json

```json
// appsettings.json — CHỈ có config không nhạy cảm
{
  "Logging": { "LogLevel": { "Default": "Information" } },
  "Urls": "http://+:5001"
}
```

Tất cả secrets qua environment variables:
```bash
JWT__SECRET=my-super-secret-key-min-32-chars!!
DB__IDENTITY=Host=localhost;Port=5433;...
```

### 2. Error messages không lộ thông tin

```csharp
// Login thất bại — KHÔNG nói "email không tồn tại" hay "password sai"
return Result.Failure<AuthTokens>("Invalid email or password.");
```

Tại sao? Nếu nói "email không tồn tại" → attacker biết email nào KHÔNG có trong hệ thống → thu hẹp phạm vi attack.

### 3. CorrelationId trong mọi error response

```json
{
  "error": "An unexpected error occurred.",
  "correlationId": "a1b2c3d4-5e6f-7890-abcd-ef1234567890"
}
```

Client gửi correlationId cho support → dev tìm log chính xác → fix nhanh hơn.

### 4. BCrypt workFactor = 12

Không quá chậm (user chờ được), không quá nhanh (attacker brute-force khó). Khi phần cứng mạnh hơn, tăng workFactor lên 13, 14...

---

## Tóm tắt

1. **JWT** chứa claims (sub, email, role), signed bằng HS256, hết hạn 15 phút
2. **Password** hash bằng BCrypt workFactor 12, chỉ lưu hash, không lưu plaintext
3. **Refresh token** là random bytes, lưu SHA-256 hash trong DB, rotation khi refresh
4. **RBAC**: `[Authorize(Roles = "Admin")]` — dựa trên role
5. **ABAC**: OwnerId check — customer chỉ truy cập resource của mình
6. **Login error** không phân biệt "email sai" vs "password sai" — chống enumeration
7. **Secrets** chỉ qua env vars, không bao giờ trong appsettings.json

> **Tiếp theo**: [Bài 7: Testing](./07-testing.md) — Cách viết unit test cho domain layer.
