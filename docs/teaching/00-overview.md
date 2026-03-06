# Bài 0: Tổng Quan Project FinCore

> **Dành cho**: Junior Developer (~2 năm kinh nghiệm) muốn hiểu kiến trúc enterprise-grade
> **Thời gian đọc**: ~15 phút

## Bạn sẽ học được gì

- FinCore là gì và tại sao nó tồn tại
- Các architectural pattern được sử dụng (DDD, CQRS, Clean Architecture)
- Cấu trúc thư mục project
- Cách các layer phụ thuộc nhau
- Cách setup và chạy project lần đầu

---

## FinCore là gì?

FinCore là một **backend pet project** mô phỏng nền tảng dữ liệu tài chính (financial data platform), được xây dựng bằng C# .NET 10. Mục tiêu chính không phải là tạo ra sản phẩm thương mại, mà là **thực hành các pattern kiến trúc enterprise** mà bạn sẽ gặp khi làm việc ở các công ty lớn:

- **Domain-Driven Design (DDD)** — thiết kế phần mềm xoay quanh business logic
- **CQRS** — tách biệt đọc và ghi dữ liệu
- **Event Sourcing** — lưu trạng thái dưới dạng chuỗi sự kiện (Phase 2+)
- **Microservices** — tách ứng dụng thành các service độc lập
- **Clean Architecture** — tổ chức code theo tầng với dependency hướng vào trong

## Tại sao cần học những pattern này?

Khi bạn mới bắt đầu code, bạn thường viết mọi thứ trong một project duy nhất — controller gọi thẳng database, business logic nằm rải rác khắp nơi. Cách này hoạt động tốt cho project nhỏ, nhưng khi hệ thống lớn lên (hàng trăm API, hàng chục developer cùng làm), nó sẽ trở thành **mớ hỗn độn** (spaghetti code) không thể maintain được.

Các pattern trong FinCore giải quyết vấn đề đó bằng cách:

| Vấn đề | Pattern giải quyết |
|---|---|
| Business logic rải rác khắp nơi | **DDD** — gom business logic vào Aggregate |
| Đọc và ghi có requirements khác nhau | **CQRS** — tách read model và write model |
| Không biết ai phụ thuộc ai | **Clean Architecture** — dependency chỉ hướng vào trong |
| Một thay đổi break hết mọi thứ | **Microservices** — mỗi service độc lập, database riêng |

---

## Kiến trúc tổng thể

### Hai microservices (Phase 1)

```
┌─────────────────────┐     ┌─────────────────────┐
│   Identity Service  │     │  Accounts Service    │
│     (Port 5001)     │     │    (Port 5002)       │
│                     │     │                      │
│  - Đăng ký user     │     │  - Mở tài khoản     │
│  - Đăng nhập        │     │  - Nạp/Rút tiền     │
│  - JWT tokens       │     │  - Đóng băng/Đóng TK│
│  - Refresh tokens   │     │  - Xem TK            │
└────────┬────────────┘     └────────┬─────────────┘
         │                           │
         └────────┬──────────────────┘
                  │
         ┌────────▼────────┐
         │   PostgreSQL    │
         │   (Port 5433)   │
         │                 │
         │ fincore_identity│
         │ fincore_accounts│
         └─────────────────┘
```

**Quan trọng**: Hai service này có **database riêng biệt** (không share table). Đây là nguyên tắc cơ bản của microservices — mỗi service sở hữu data của mình.

### Bốn tầng trong mỗi service

Mỗi microservice được tổ chức thành **4 layer** với quy tắc dependency nghiêm ngặt:

```
┌─────────────────────────────────────────────────┐
│                    API Layer                     │
│  (Controllers, Middleware, Program.cs)           │
│  Nhận HTTP request → trả HTTP response           │
├────────────┬────────────────────────────────────┤
│            │         Infrastructure Layer        │
│            │  (EF Core, BCrypt, JWT service)     │
│            │  Triển khai các interface            │
│            ├────────────────────────────────────┤
│            ▼                                     │
│         Application Layer                        │
│  (Commands, Queries, Handlers, Validators)       │
│  Điều phối workflow, gọi Domain                   │
├──────────────────────────────────────────────────┤
│                  Domain Layer                     │
│  (Aggregates, Value Objects, Events, Exceptions) │
│  Business logic thuần túy, KHÔNG phụ thuộc gì     │
├──────────────────────────────────────────────────┤
│                  SharedKernel                     │
│  (AggregateRoot, ValueObject, Result, Money...)  │
│  Building blocks dùng chung giữa các service      │
└──────────────────────────────────────────────────┘
```

### Quy tắc dependency (CỰC KỲ QUAN TRỌNG)

```
API  ───►  Application  ───►  Domain  ───►  SharedKernel
 │              │
 └──────────►  Infrastructure
```

- **Domain** KHÔNG BIẾT gì về database, HTTP, hay bất kỳ framework nào. Nó chỉ chứa business logic thuần túy.
- **Application** define interface (VD: `IAccountRepository`), nhưng KHÔNG biết ai triển khai.
- **Infrastructure** triển khai interface đó (VD: `EfAccountRepository` dùng EF Core).
- **API** nối tất cả lại với nhau (Dependency Injection).

**Tại sao phải làm vậy?** Vì khi bạn muốn đổi database từ PostgreSQL sang MongoDB, bạn chỉ cần thay đổi Infrastructure layer — Domain và Application **không cần sửa một dòng code nào**.

---

## Cấu trúc thư mục

```
FinCore/
├── src/
│   ├── shared/                          # Code dùng chung
│   │   ├── FinCore.SharedKernel/        # Base classes: AggregateRoot, ValueObject, Result<T>...
│   │   ├── FinCore.EventBus.Abstractions/  # Interface cho event bus (Kafka sẽ dùng ở Phase 2)
│   │   └── FinCore.Observability/       # Logging, tracing, health checks
│   │
│   └── services/
│       ├── FinCore.Identity/            # Identity microservice
│       │   ├── FinCore.Identity.Api/           # Controllers, middleware, startup
│       │   ├── FinCore.Identity.Application/   # Commands, queries, handlers
│       │   ├── FinCore.Identity.Domain/        # User aggregate, value objects
│       │   └── FinCore.Identity.Infrastructure/ # EF Core, BCrypt, JWT
│       │
│       └── FinCore.Accounts/            # Accounts microservice (cấu trúc tương tự)
│           ├── FinCore.Accounts.Api/
│           ├── FinCore.Accounts.Application/
│           ├── FinCore.Accounts.Domain/
│           └── FinCore.Accounts.Infrastructure/
│
├── tests/unit/                          # Unit tests (domain layer only)
├── infra/                               # Docker Compose (PostgreSQL, Redis, Kafka, Seq)
├── scripts/                             # Database initialization scripts
├── docs/                                # Tài liệu
├── Directory.Build.props                # Cấu hình chung cho tất cả projects (.NET 10, Nullable...)
└── FinCore.slnx                         # Solution file (format mới .slnx, không phải .sln)
```

---

## Công nghệ sử dụng

| Công nghệ | Vai trò | Phiên bản |
|---|---|---|
| .NET 10 | Runtime & SDK | 10.0 |
| PostgreSQL | Database | 16 (Alpine) |
| Entity Framework Core | ORM (Object-Relational Mapping) | 10.0.0 |
| MediatR | Mediator pattern cho CQRS | 14.1.0 |
| FluentValidation | Input validation | 12.1.1 |
| BCrypt.Net | Password hashing | 4.1.0 |
| Serilog | Structured logging | 10.0.0 |
| OpenTelemetry | Distributed tracing | 1.15.0 |
| Seq | Log aggregation & UI | latest |
| xUnit + FluentAssertions | Unit testing | 2.9.3 / 8.8.0 |
| Docker Compose | Local infrastructure | - |

---

## Hướng dẫn chạy project lần đầu

### 1. Khởi động infrastructure

```bash
docker compose -f infra/docker-compose.yml up -d
```

Lệnh này sẽ start: PostgreSQL (port 5433), Redis (6379), Kafka (9092), Seq (8080/5341).

### 2. Build solution

```bash
dotnet build FinCore.slnx
```

### 3. Apply database migrations

```bash
dotnet ef database update \
  --project src/services/FinCore.Identity/FinCore.Identity.Infrastructure \
  --startup-project src/services/FinCore.Identity/FinCore.Identity.Api

dotnet ef database update \
  --project src/services/FinCore.Accounts/FinCore.Accounts.Infrastructure \
  --startup-project src/services/FinCore.Accounts/FinCore.Accounts.Api
```

### 4. Chạy services

```bash
# Terminal 1 — Identity Service
dotnet run --project src/services/FinCore.Identity/FinCore.Identity.Api

# Terminal 2 — Accounts Service
dotnet run --project src/services/FinCore.Accounts/FinCore.Accounts.Api
```

### 5. Chạy tests

```bash
dotnet test FinCore.slnx
```

### 6. Xem logs

Mở browser tại `http://localhost:8080` để xem Seq UI — nơi tập trung tất cả structured logs.

---

## Lộ trình đọc tài liệu

Bộ tài liệu này được thiết kế theo thứ tự từ nền tảng → chi tiết → tổng hợp:

| Bài | Chủ đề | Mô tả |
|---|---|---|
| **01** | [SharedKernel](./01-shared-kernel.md) | Building blocks: AggregateRoot, ValueObject, Result, Money |
| **02** | [Domain Layer](./02-domain-layer.md) | Aggregates, business rules, events, exceptions |
| **03** | [Application Layer](./03-application-layer.md) | CQRS, MediatR, commands, queries, validators |
| **04** | [Infrastructure Layer](./04-infrastructure-layer.md) | EF Core, repositories, aggregate reconstitution |
| **05** | [API Layer](./05-api-layer.md) | Controllers, middleware, startup pipeline |
| **06** | [Security](./06-security.md) | JWT, password hashing, refresh tokens, ABAC |
| **07** | [Testing](./07-testing.md) | Unit tests, FluentAssertions, test patterns |
| **08** | [Observability](./08-observability.md) | Serilog, correlation ID, tracing, health checks |
| **09** | [Request Lifecycle](./09-request-lifecycle.md) | End-to-end walkthrough: từ HTTP request đến response |

**Khuyến nghị**: Đọc theo thứ tự từ bài 01 đến 09. Mỗi bài xây dựng trên kiến thức của bài trước.

---

## Tóm tắt

- FinCore gồm 2 microservices (Identity + Accounts), mỗi cái có 4 layers
- Dependency chỉ chạy một chiều: API → Application → Domain ← Infrastructure
- Domain layer **không phụ thuộc bất kỳ framework nào** — chỉ chứa business logic thuần túy
- Mỗi service có database riêng, không share tables
- Project sử dụng .NET 10, PostgreSQL, EF Core, MediatR, Serilog, xUnit
