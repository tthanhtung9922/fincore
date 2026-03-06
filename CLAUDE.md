# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project

**FinCore** — a C# .NET 10 backend pet project simulating a financial data platform. The goal is to practice enterprise architecture patterns: DDD, CQRS, Event Sourcing, microservices, data lake pipelines, and finance/banking security. See `PROJECT.md` for the full specification and `docs/` for implementation guides.

**Phase 1 is complete and fully operational.** See `docs/phase-1-journey.md` for a detailed walkthrough.

## Environment

- **SDK**: .NET 10.0 (`net10.0` target framework everywhere via `Directory.Build.props`)
- **Solution file**: `FinCore.slnx` (new `.slnx` format — not `.sln`)
- **Local Postgres**: Port **5433** (5432 is occupied by a local PostgreSQL 18 install)

## Commands

```bash
# Restore dependencies
dotnet restore FinCore.slnx

# Build entire solution
dotnet build FinCore.slnx

# Run Identity service (port 5001)
dotnet run --project src/services/FinCore.Identity/FinCore.Identity.Api

# Run Accounts service (port 5002)
dotnet run --project src/services/FinCore.Accounts/FinCore.Accounts.Api

# Run all unit tests
dotnet test FinCore.slnx

# Run tests for a single project
dotnet test tests/unit/FinCore.Identity.Domain.Tests/FinCore.Identity.Domain.Tests.csproj
dotnet test tests/unit/FinCore.Accounts.Domain.Tests/FinCore.Accounts.Domain.Tests.csproj

# Run a specific test class
dotnet test tests/unit/FinCore.Accounts.Domain.Tests/FinCore.Accounts.Domain.Tests.csproj --filter "FullyQualifiedName~AccountAggregateTests"

# Start infrastructure (PostgreSQL on 5433, Redis, Kafka, Seq)
docker compose -f infra/docker-compose.yml up -d

# Apply EF Core migrations (run once after first clone or after adding a new migration)
dotnet ef database update --project src/services/FinCore.Identity/FinCore.Identity.Infrastructure --startup-project src/services/FinCore.Identity/FinCore.Identity.Api
dotnet ef database update --project src/services/FinCore.Accounts/FinCore.Accounts.Infrastructure --startup-project src/services/FinCore.Accounts/FinCore.Accounts.Api

# Generate a new migration after changing a domain model
dotnet ef migrations add <MigrationName> --project src/services/FinCore.Identity/FinCore.Identity.Infrastructure --startup-project src/services/FinCore.Identity/FinCore.Identity.Api
dotnet ef migrations add <MigrationName> --project src/services/FinCore.Accounts/FinCore.Accounts.Infrastructure --startup-project src/services/FinCore.Accounts/FinCore.Accounts.Api
```

## Key Package Versions (Phase 1)

| Package | Version |
|---|---|
| Microsoft.EntityFrameworkCore | 10.0.0 |
| Npgsql.EntityFrameworkCore.PostgreSQL | 10.0.0 |
| MediatR | 14.1.0 |
| FluentValidation | 12.1.1 |
| Serilog.AspNetCore | 10.0.0 |
| OpenTelemetry | 1.15.0 |
| BCrypt.Net-Next | 4.1.0 |
| Scalar.AspNetCore | latest |
| xUnit + FluentAssertions + Moq + Bogus | (test projects) |

## Environment Variables

All secrets come from environment variables, never from `appsettings.json`.

**Identity service** (`src/services/FinCore.Identity/FinCore.Identity.Api`):
```
DB__IDENTITY=Host=localhost;Port=5433;Database=fincore_identity;Username=fincore;Password=fincore_dev
JWT__SECRET=<min 32 chars>
JWT__ISSUER=fincore-identity
JWT__AUDIENCE=fincore
JWT__ACCESS_TOKEN_EXPIRY_MINUTES=15
JWT__REFRESH_TOKEN_EXPIRY_DAYS=7
OBSERVABILITY__SEQ_URL=http://localhost:5341
```

**Accounts service** (`src/services/FinCore.Accounts/FinCore.Accounts.Api`):
```
DB__ACCOUNTS=Host=localhost;Port=5433;Database=fincore_accounts;Username=fincore;Password=fincore_dev
OBSERVABILITY__SEQ_URL=http://localhost:5341
```

Dev fallbacks are hardcoded in the Infrastructure `AddXxxInfrastructure()` extension methods so local runs work without setting any env vars. Seq UI is available at `http://localhost:8080`.

## Architecture

### What is built (Phase 1)

```
src/shared/
  FinCore.SharedKernel/          — AggregateRoot, ValueObject, DomainEvent, Entity, Money, Iban, Result<T>, PagedList<T>
  FinCore.EventBus.Abstractions/ — IEventBus, IntegrationEvent, NoOpEventBus (Kafka deferred to Phase 2)
  FinCore.Observability/         — Serilog setup, OpenTelemetry tracing, CorrelationIdMiddleware, health check helpers

src/services/FinCore.Identity/
  FinCore.Identity.Domain/       — User aggregate, Email VO, HashedPassword VO, RefreshToken entity, domain events & exceptions
  FinCore.Identity.Application/  — Register/Login/RefreshToken/RevokeToken commands; GetUserById query; IPasswordHasher, IJwtTokenService interfaces
  FinCore.Identity.Infrastructure/ — EF Core (IdentityDbContext), BCryptPasswordHasher, JwtTokenService, EfUserRepository
  FinCore.Identity.Api/          — AuthController (/api/v1/auth), UsersController (/api/v1/users), port 5001

src/services/FinCore.Accounts/
  FinCore.Accounts.Domain/       — Account aggregate, AccountNumber VO, Money (from SharedKernel), domain events & exceptions
  FinCore.Accounts.Application/  — OpenAccount/Deposit/Withdraw/Freeze/Close commands; GetAccountById/GetAccountsByOwner queries
  FinCore.Accounts.Infrastructure/ — EF Core (AccountsDbContext), EfAccountRepository, AccountReadRepository
  FinCore.Accounts.Api/          — AccountsController (/api/v1/accounts), port 5002

tests/unit/
  FinCore.Identity.Domain.Tests/ — 18 pure domain unit tests (UserAggregate, RefreshToken, Email VO)
  FinCore.Accounts.Domain.Tests/ — 16 pure domain unit tests (AccountAggregate, Money VO)

infra/
  docker-compose.yml             — postgres:16-alpine (5433), redis:7-alpine (6379), kafka KRaft (9092), seq (8080/5341)
  docker-compose.override.yml    — per-dev overrides (gitignored)

scripts/
  init-db.sh                     — creates fincore_identity and fincore_accounts databases on first postgres start
```

### Layer Structure

Each microservice has four layers with strict dependency direction:

```
*.Api  ──►  *.Application  ──►  *.Domain  ──►  FinCore.SharedKernel
 │               │
 └──────────────►*.Infrastructure
```

- `*.Domain` — aggregates, value objects, domain events, exceptions; **zero NuGet dependencies** (only SharedKernel project reference)
- `*.Application` — CQRS command/query handlers via MediatR; defines interfaces (`IUserRepository`, `IPasswordHasher`) that Infrastructure implements
- `*.Infrastructure` — EF Core, BCrypt, JWT; implements Application interfaces; never referenced by Domain
- `*.Api` — ASP.NET Core wiring; reads JWT `sub` claim to extract current user; maps results to HTTP responses

### Core Patterns (Phase 1 implementation)

**DDD Aggregates** — `Account` and `User` are aggregate roots. All state changes go through domain methods (`account.Deposit(...)`, `user.Login()`). Methods raise domain events via `RaiseEvent()`. The `Apply()` switch updates internal state from the event data (single source of truth, testable without the DB). Factory methods (`Account.Open()`, `User.Register()`) replace public constructors.

**Value Objects** — `Money`, `Email`, `AccountNumber`, `HashedPassword` are immutable sealed classes with protected constructors; equality is by value not reference (`GetEqualityComponents()` override). They validate their invariants on construction and throw `DomainException` if invalid.

**CQRS** — Commands mutate state (load aggregate → call domain method → save → return `Result`). Queries bypass the aggregate entirely and project directly from the EF read model into DTOs (`AccountReadRepository` uses `Select()` to project without loading the full entity graph). Each service has separate `IXxxRepository` (write) and `IXxxReadRepository` (read) interfaces.

**CQRS File Layout** — One folder per command/query under `Application/Commands/` or `Application/Queries/`, each containing:
- `XxxCommand.cs` — `record XxxCommand(...) : IRequest<Result<T>>`
- `XxxCommandHandler.cs` — `IRequestHandler<XxxCommand, Result<T>>`
- `XxxCommandValidator.cs` — `AbstractValidator<XxxCommand>` (commands only)

**Result pattern** — `Result<T>` / `Result` express success or failure without exceptions crossing the Application→Api boundary. Domain exceptions are caught by `ExceptionHandlingMiddleware` and mapped to 4xx responses.

**JWT authentication** — HS256 tokens signed with `JWT__SECRET`. Claims: `sub` (userId), `email`, `role`, `jti`. Refresh tokens are opaque random strings stored as SHA-256 hashes. Access tokens expire in 15 min; refresh tokens in 7 days. The Accounts service validates tokens issued by Identity using the shared secret.

**ABAC in Controllers** — Customers may only access their own resources. Controllers extract `OwnerId` from the JWT `sub` claim and compare with the resource's owner; Admin/Analyst roles bypass this check.

**Structured logging** — Serilog enriches every log line with `ServiceName`, `MachineName`, `ThreadId`, and `CorrelationId` (from `X-Correlation-Id` header or auto-generated). Writes to console + Seq.

**Health checks** — `/health/live` (liveness) and `/health/ready` (checks PostgreSQL connectivity).

### EF Core Entity Mapping

Value objects and enums are **flattened** into EF entity classes (e.g., `Money` → `BalanceAmount` + `BalanceCurrency` columns; enums stored as strings). Entity configurations live in one `IEntityTypeConfiguration<T>` class per entity, auto-discovered by `ApplyConfigurationsFromAssembly()`. Aggregate `Version` is used as an optimistic concurrency token.

### Aggregate Reconstitution from DB

`EfAccountRepository` uses `RuntimeHelpers.GetUninitializedObject(typeof(Account))` to create an Account instance without calling any constructor, then populates properties via reflection. The `_domainEvents` private list in `AggregateRoot` must be explicitly initialized via reflection after this call — otherwise any subsequent domain method that calls `RaiseEvent()` will throw `NullReferenceException`.

`EfUserRepository` uses `User.Register()` (real constructor chain) and then overwrites `Id` via reflection to avoid the need for `GetUninitializedObject`.

### Domain Events & Event Bus

Command handlers call `aggregate.ClearDomainEvents()` after saving. This is intentional — Phase 2 will introduce the Outbox pattern to publish events reliably. `IEventBus` is currently a `NoOpEventBus` (no-op publish). Do not change this pattern prematurely.

### Security Conventions

- No secrets in `appsettings.json` — all secrets via env vars (or HashiCorp Vault in later phases)
- `HashedPassword` value object is a pure string wrapper — BCrypt lives in Infrastructure only (`BcryptPasswordHasher`), never in Domain
- Refresh tokens stored as SHA-256 hashes in the DB; raw token returned to client only once at login
- `ExceptionHandlingMiddleware` maps `DomainException` → 400, unhandled → 500; always includes `correlationId` in error body

### Service Communication (Phase 1)

- REST only (no gRPC, no Kafka yet — planned Phase 2+)
- Correlation ID propagated via `X-Correlation-Id` header
- No FK constraints between services — Accounts has no DB-level reference to Identity (cross-service data matched by ID in application layer)

### Testing Conventions

- **Unit tests** — domain layer only; no mocks needed because aggregates are pure functions of events. Test by calling domain methods and asserting `DomainEvents` and property state.
- **Assertion style** — FluentAssertions: `result.Should().Be(...)`, `act.Should().Throw<DomainException>()`
- **Integration tests** — planned Phase 2 (Testcontainers)
- **Test data** — inline in tests; Bogus (Faker) to be added in Phase 2

### Phase 2 Roadmap (what comes next)

Phase 2 adds event-driven infrastructure: Kafka integration (replacing `NoOpEventBus`), Outbox pattern (reliable event publishing), a new Ledger service (fully event-sourced), Redis read projections, and OpenTelemetry → Jaeger export. Later phases add data lake, resilience (Polly), field-level encryption, YARP gateway, saga orchestration, Prometheus/Grafana, and Kubernetes.
