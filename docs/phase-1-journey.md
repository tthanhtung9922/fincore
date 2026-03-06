# FinCore — Phase 1: Complete Journey

> A beginner-friendly walkthrough of everything built in Phase 1: what it is, why it was built this way, and how every piece fits together.

---

## Table of Contents

1. [What We Built](#1-what-we-built)
2. [Why These Patterns?](#2-why-these-patterns)
3. [Repository Layout](#3-repository-layout)
4. [The Foundation: SharedKernel](#4-the-foundation-sharedkernel)
5. [The Four-Layer Architecture](#5-the-four-layer-architecture)
6. [Identity Service — Deep Dive](#6-identity-service--deep-dive)
7. [Accounts Service — Deep Dive](#7-accounts-service--deep-dive)
8. [Infrastructure: Connecting Domain to the Database](#8-infrastructure-connecting-domain-to-the-database)
9. [The API Layer: Exposing Behaviour Over HTTP](#9-the-api-layer-exposing-behaviour-over-http)
10. [Observability: Logging, Tracing, Health Checks](#10-observability-logging-tracing-health-checks)
11. [Docker Compose: Local Infrastructure](#11-docker-compose-local-infrastructure)
12. [Unit Tests: Proving the Domain Works](#12-unit-tests-proving-the-domain-works)
13. [Running Everything End-to-End](#13-running-everything-end-to-end)
14. [Bugs We Hit and How We Fixed Them](#14-bugs-we-hit-and-how-we-fixed-them)
15. [What Comes Next (Phase 2 Preview)](#15-what-comes-next-phase-2-preview)

---

## 1. What We Built

Phase 1 delivered a working multi-service .NET 10 backend with two independent microservices:

| Service | Port | What it does |
|---|---|---|
| **Identity** | 5001 | Register users, log in, issue JWT access + refresh tokens, role management |
| **Accounts** | 5002 | Open bank accounts, deposit, withdraw, freeze, close, query balances |

Both services are backed by a real PostgreSQL database running in Docker, emit structured logs to the Seq log aggregator, and expose `/health/live` and `/health/ready` endpoints.

**End-to-end verified flow:**
```
POST /api/v1/auth/register  → 201 {"id": "..."}
POST /api/v1/auth/login     → 200 {"accessToken":"eyJ...", "refreshToken":"...", "expiresAt":"..."}
POST /api/v1/accounts       → 201 {"id": "..."}    (requires JWT)
POST /api/v1/accounts/{id}/deposit   → 200
POST /api/v1/accounts/{id}/withdraw  → 200
GET  /api/v1/accounts/{id}  → 200 {"balance": 300.0000, ...}
```

---

## 2. Why These Patterns?

Before diving into code, it helps to understand *why* each architectural decision was made.

### Domain-Driven Design (DDD)

A traditional approach puts business logic in service classes or controllers:

```csharp
// BAD: business logic scattered in a service
public class AccountService
{
    public void Withdraw(Account account, decimal amount)
    {
        if (account.Balance < amount) throw new Exception("Not enough money");
        account.Balance -= amount;
    }
}
```

DDD says: the *account itself* should know the rules about withdrawing. The object that owns the data owns the behaviour. This makes the logic easy to test (no database needed) and impossible to bypass.

```csharp
// GOOD: business logic lives on the aggregate
account.Withdraw(200m, "REF-002");  // account enforces all rules internally
```

### Clean Architecture (The Four Layers)

The project separates concerns into four layers per service, with a strict dependency rule: **inner layers know nothing about outer layers**.

```
Domain  ←──  Application  ←──  Infrastructure
                                      ↑
                               Api ───┘
```

- `Domain` doesn't know about databases, HTTP, or NuGet packages
- `Application` doesn't know whether the database is PostgreSQL or SQL Server
- `Infrastructure` can be swapped out (e.g., replace PostgreSQL with MongoDB) without touching Domain or Application

### CQRS (Command Query Responsibility Segregation)

A "Command" changes something (deposit money). A "Query" reads something (get balance). They are handled by completely separate code paths. This lets you:
- Optimise reads independently from writes (queries can use projections/views instead of loading full aggregates)
- Add read replicas later without touching command code

### Result Pattern (No Exceptions for Business Failures)

When a user tries to withdraw more than their balance, that is not an unexpected crash — it is a *known business rule violation*. Throwing an exception and catching it in a controller is expensive and awkward. Instead:

```csharp
// Application handler returns a Result, not throws
if (Balance.Amount < amount)
    return Result.Failure("Insufficient funds.");

// Controller checks the result
if (!result.IsSuccess)
    return BadRequest(new { error = result.Error });
```

---

## 3. Repository Layout

```
FinCore/
├── Directory.Build.props          # Global: net10.0, Nullable, ImplicitUsings
├── FinCore.slnx                   # Solution file (.slnx format, not .sln)
│
├── src/
│   ├── shared/
│   │   ├── FinCore.SharedKernel/          # Base classes used by every service
│   │   ├── FinCore.EventBus.Abstractions/ # IEventBus interface + NoOpEventBus (Kafka in Phase 2)
│   │   └── FinCore.Observability/         # Serilog, OpenTelemetry, middleware
│   │
│   └── services/
│       ├── FinCore.Identity/
│       │   ├── FinCore.Identity.Domain/
│       │   ├── FinCore.Identity.Application/
│       │   ├── FinCore.Identity.Infrastructure/
│       │   └── FinCore.Identity.Api/
│       │
│       └── FinCore.Accounts/
│           ├── FinCore.Accounts.Domain/
│           ├── FinCore.Accounts.Application/
│           ├── FinCore.Accounts.Infrastructure/
│           └── FinCore.Accounts.Api/
│
├── tests/
│   └── unit/
│       ├── FinCore.Identity.Domain.Tests/
│       └── FinCore.Accounts.Domain.Tests/
│
├── infra/
│   ├── docker-compose.yml
│   └── docker-compose.override.yml
│
├── scripts/
│   └── init-db.sh                 # Creates both databases on first Postgres start
│
└── docs/
    └── phase-1-journey.md         # (this file)
```

**`Directory.Build.props`** — a special MSBuild file that applies settings to every `.csproj` in the repo without repeating them. We set:
```xml
<TargetFramework>net10.0</TargetFramework>
<Nullable>enable</Nullable>
<ImplicitUsings>enable</ImplicitUsings>
```

---

## 4. The Foundation: SharedKernel

`FinCore.SharedKernel` has **zero NuGet dependencies** — it is pure C#. Every other project references it. It provides the vocabulary all services speak.

### AggregateRoot

```csharp
public abstract class AggregateRoot
{
    public Guid Id { get; protected set; }
    public long Version { get; private set; }          // for optimistic concurrency

    private readonly List<DomainEvent> _domainEvents = new();
    public IReadOnlyList<DomainEvent> DomainEvents => _domainEvents.AsReadOnly();

    protected void RaiseEvent(DomainEvent @event)
    {
        _domainEvents.Add(@event);   // remember the event
        Apply(@event);               // update state from the event
        Version++;                   // increment version
    }

    protected virtual void Apply(DomainEvent @event) { }  // subclasses override this
    public void ClearDomainEvents() => _domainEvents.Clear();
}
```

**How it works:**
1. A domain method (e.g., `account.Deposit(100, "REF-001")`) calls `RaiseEvent(new MoneyDeposited(...))`
2. `RaiseEvent` adds the event to the in-memory list, then calls `Apply(event)`
3. `Apply` is where the aggregate updates its own state (e.g., `Balance = Balance + amount`)
4. After saving, `ClearDomainEvents()` is called so the list is empty for the next operation

This design means:
- All state changes are traceable to an event
- Tests can verify which events were raised, not just what the final state is
- The aggregate can later be replayed from a history of events (Event Sourcing, used in Phase 3+)

### ValueObject

```csharp
public abstract class ValueObject
{
    protected abstract IEnumerable<object?> GetEqualityComponents();

    public override bool Equals(object? obj) { ... }   // equality by components
    public override int GetHashCode() { ... }
    public static bool operator ==(ValueObject? a, ValueObject? b) { ... }
}
```

Unlike classes (which compare by reference), value objects compare by **value**. Two `Money(100, "USD")` instances are equal, just like two `int` values of `100` are equal.

Usage example: `Money`, `Email`, `AccountNumber` all extend `ValueObject`.

### DomainEvent

```csharp
public abstract record DomainEvent
{
    public Guid EventId { get; init; } = Guid.NewGuid();
    public DateTimeOffset OccurredAt { get; init; } = DateTimeOffset.UtcNow;
    public int SchemaVersion { get; init; } = 1;
}
```

A `record` in C# is immutable by default — perfect for events that describe something that already happened and should never change. `SchemaVersion` supports future event schema evolution.

### Money Value Object

```csharp
public sealed class Money : ValueObject
{
    public decimal Amount { get; }
    public string CurrencyCode { get; }    // "USD", "EUR", etc.

    public Money(decimal amount, string currencyCode) { ... validates 3-letter ISO code }

    public static Money operator +(Money a, Money b)
    {
        if (a.CurrencyCode != b.CurrencyCode)
            throw new InvalidOperationException("Cannot add different currencies");
        return new Money(a.Amount + b.Amount, a.CurrencyCode);
    }
}
```

`Money` cannot be added to money of a different currency — this is enforced at the type level, making an entire class of bugs impossible.

### Result\<T\>

```csharp
public class Result<T> : Result
{
    public T Value => IsSuccess ? _value! : throw new InvalidOperationException("...");
    public static Result<T> Success(T value) => ...
    public static Result<T> Failure(string error) => ...
}
```

Used throughout Application handlers to return either a value or an error message without throwing exceptions. The API layer checks `result.IsSuccess` and maps to the appropriate HTTP response.

---

## 5. The Four-Layer Architecture

Here is how data flows through the system for a `POST /api/v1/accounts/{id}/deposit` request:

```
HTTP Request
    │
    ▼
AccountsController.Deposit()         [Api layer]
    │ creates DepositCommand(accountId, 500, "USD", "REF-001")
    ▼
IMediator.Send(command)              [MediatR dispatches to handler]
    │
    ▼
DepositCommandHandler.Handle()       [Application layer]
    │ 1. loads Account from IAccountRepository
    │ 2. calls account.Deposit(500, "REF-001")
    │ 3. saves via IAccountRepository.UpdateAsync()
    │ 4. returns Result.Success()
    ▼
Account.Deposit()                    [Domain layer]
    │ validates: status must be Active, amount must be positive
    │ raises MoneyDeposited event
    │ Apply(MoneyDeposited) → Balance += 500
    ▼
EfAccountRepository.UpdateAsync()    [Infrastructure layer]
    │ updates AccountEntity.BalanceAmount in EF Core
    │ SaveChangesAsync()
    ▼
PostgreSQL (fincore_accounts)
```

Notice that the `Account` class never imports anything from EF Core or ASP.NET Core. The domain is completely isolated.

### Project Reference Graph

```
FinCore.Identity.Api
  ├── FinCore.Identity.Application
  ├── FinCore.Identity.Infrastructure
  └── FinCore.Observability

FinCore.Identity.Application
  ├── FinCore.Identity.Domain
  └── FinCore.EventBus.Abstractions

FinCore.Identity.Infrastructure
  ├── FinCore.Identity.Application   (for interfaces like IUserRepository)
  └── FinCore.SharedKernel

FinCore.Identity.Domain
  └── FinCore.SharedKernel           (ONLY this reference allowed)
```

---

## 6. Identity Service — Deep Dive

### Domain: The User Aggregate

The `User` aggregate enforces all identity business rules.

```csharp
public class User : AggregateRoot
{
    public Email Email { get; private set; }
    public HashedPassword PasswordHash { get; private set; }
    public UserRole Role { get; private set; }
    public bool IsActive { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    private readonly List<RefreshToken> _refreshTokens = new();

    // Factory method — the only public way to create a User
    public static User Register(Email email, HashedPassword passwordHash, UserRole role)
    {
        var user = new User();
        user.RaiseEvent(new UserRegistered(Guid.NewGuid(), email.Value, role.ToString()));
        user.Email = email;
        user.PasswordHash = passwordHash;
        user.Role = role;
        return user;
    }

    // Apply sets state from the event
    protected override void Apply(DomainEvent @event)
    {
        if (@event is UserRegistered registered)
        {
            Id = registered.UserId;       // Id comes from the event
            IsActive = true;
            CreatedAt = registered.OccurredAt;
        }
    }
}
```

**Key design decisions:**

- The constructor is `private`. The only way to create a `User` is via `Register()`, which guarantees that a `UserRegistered` event is always raised when a user is created.
- `Email` is a value object, not a `string`. You cannot pass an invalid email — the `Email` constructor throws if the format is wrong.
- `HashedPassword` is also a value object — a pure string wrapper. The actual BCrypt hashing happens in `BcryptPasswordHasher` (Infrastructure), not in the domain. The domain simply stores "some hashed string" without knowing how it was hashed.

### Domain: Value Objects

**Email:**
```csharp
public sealed class Email : ValueObject
{
    public string Value { get; }

    public Email(string value)
    {
        // validates with regex on construction
        // stores as lowercase
        Value = value.ToLowerInvariant();
    }
}
```

**HashedPassword** — a pure wrapper (no BCrypt dependency in Domain):
```csharp
public sealed class HashedPassword : ValueObject
{
    public string Value { get; }
    public HashedPassword(string hashedValue) { Value = hashedValue; }
}
```

**RefreshToken** — not a value object but an Entity (has its own `Id`, owned by `User`):
```csharp
public class RefreshToken : Entity
{
    public string TokenHash { get; private set; }   // SHA-256 hash of the actual token
    public DateTimeOffset ExpiresAt { get; private set; }
    public bool IsRevoked { get; private set; }
    public bool IsExpired => DateTimeOffset.UtcNow >= ExpiresAt;
    public bool IsActive => !IsRevoked && !IsExpired;

    public void Revoke(string? replacedByToken = null) { ... }
}
```

### Application: Commands and Handlers

Each use case is a Command + Handler pair. MediatR routes commands to handlers automatically via DI.

**RegisterUserCommand:**
```csharp
public record RegisterUserCommand(string Email, string Password, string Role)
    : IRequest<Result<Guid>>;
```

**RegisterUserCommandHandler** — the full use case logic:
```csharp
public async Task<Result<Guid>> Handle(RegisterUserCommand request, CancellationToken ct)
{
    var email = new Email(request.Email);           // validates format

    var existingUser = await _userRepository.GetByEmailAsync(email, ct);
    if (existingUser is not null)
        return Result.Failure<Guid>("Email already registered.");

    if (!Enum.TryParse<UserRole>(request.Role, true, out var role))
        return Result.Failure<Guid>($"Invalid role: {request.Role}");

    var hashedPassword = new HashedPassword(_passwordHasher.Hash(request.Password));
    var user = User.Register(email, hashedPassword, role);

    await _userRepository.AddAsync(user, ct);

    return Result.Success(user.Id);
}
```

Note how clean this is: the handler doesn't know whether the password is BCrypt or SHA. It calls `_passwordHasher.Hash()` through an interface defined in Application, implemented in Infrastructure.

**FluentValidation** runs before the handler and rejects bad input before it even reaches the handler:
```csharp
public class RegisterUserCommandValidator : AbstractValidator<RegisterUserCommand>
{
    public RegisterUserCommandValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(256);
        RuleFor(x => x.Password).NotEmpty().MinimumLength(12);
        RuleFor(x => x.Role).Must(r => Enum.TryParse<UserRole>(r, true, out _));
    }
}
```

### Application: JWT

`IJwtTokenService` is an interface in Application. The concrete `JwtTokenService` lives in Infrastructure. It generates HS256-signed JWTs with four claims:

| Claim | Value |
|---|---|
| `sub` | User's Guid Id |
| `email` | User's email address |
| `role` | e.g., "Admin" |
| `jti` | Random Guid per token (JWT ID, prevents replay) |

Refresh tokens are random 64-byte values (base64-encoded). The raw value is returned to the client once. A SHA-256 hash of it is stored in the database. This means if the database is breached, the attacker gets hashes, not tokens.

### Infrastructure: EF Core and the Entity Separation

A common mistake is to use domain aggregates directly as EF Core entities. This creates tight coupling — the domain object must have public setters and parameterless constructors to satisfy EF.

Instead, we use a **separate entity class** (`UserEntity`) for persistence, and map to/from the domain aggregate in the repository.

```
UserEntity (EF knows about) ←→ EfUserRepository ←→ User aggregate (domain)
```

`UserEntity` is a plain data class. `User` is the rich domain object. The repository translates between them.

**EF Entity configuration** (`UserEntityConfiguration`):
```csharp
builder.ToTable("users");
builder.HasKey(u => u.Id);
builder.Property(u => u.Id).ValueGeneratedNever();  // we set the Id, not the DB
builder.Property(u => u.Email).HasMaxLength(256).IsRequired();
builder.Property(u => u.Version).IsConcurrencyToken();  // optimistic concurrency
builder.HasIndex(u => u.Email).IsUnique();
builder.HasMany(u => u.RefreshTokens).WithOne(rt => rt.User).OnDelete(DeleteBehavior.Cascade);
```

**Reconstituting the aggregate from DB** (`EfUserRepository.MapToDomain`):

The challenge: `User.Register()` raises a `UserRegistered` event and sets `Id` from it. But when loading from the DB we want to reconstitute the user *without* raising spurious events. The solution:

1. Call `User.Register()` with the DB data (raises a `UserRegistered` event internally)
2. Call `user.ClearDomainEvents()` immediately to discard that phantom event
3. Use reflection to overwrite `Id` with the real database Id
4. Use reflection to add each stored `RefreshToken` to the private `_refreshTokens` list

---

## 7. Accounts Service — Deep Dive

### Domain: The Account Aggregate

```csharp
public class Account : AggregateRoot
{
    public Guid OwnerId { get; private set; }
    public AccountNumber AccountNumber { get; private set; }
    public AccountType AccountType { get; private set; }    // Checking, Savings, Investment
    public Money Balance { get; private set; }
    public string Currency { get; private set; }
    public AccountStatus Status { get; private set; }       // Active, Frozen, Closed
    public DateTimeOffset CreatedAt { get; private set; }

    // Factory method — creates a new account with zero balance
    public static Account Open(Guid ownerId, AccountType accountType, string currency)
    {
        var account = new Account();
        var accountNumber = AccountNumber.Generate();    // ACC-202603-0G7LMJ6G
        account.RaiseEvent(new AccountOpened(...));
        return account;
    }

    // Apply handles all events in a switch expression
    protected override void Apply(DomainEvent @event)
    {
        switch (@event)
        {
            case AccountOpened opened:
                Id = opened.AccountId;
                Balance = new Money(0, opened.Currency);  // starts at zero
                Status = AccountStatus.Active;
                ...
                break;
            case MoneyDeposited deposited:
                Balance = Balance + new Money(deposited.Amount, deposited.Currency);
                break;
            case MoneyWithdrawn withdrawn:
                Balance = Balance - new Money(withdrawn.Amount, withdrawn.Currency);
                break;
            case AccountFrozen:   Status = AccountStatus.Frozen;  break;
            case AccountUnfrozen: Status = AccountStatus.Active;  break;
            case AccountClosed:   Status = AccountStatus.Closed;  break;
        }
    }
}
```

**Business rules enforced by the aggregate:**

| Method | Rule |
|---|---|
| `Deposit(amount, ref)` | Status must be Active; amount must be > 0 |
| `Withdraw(amount, ref)` | Status must be Active; amount must be > 0; Balance must cover amount |
| `Freeze(reason)` | Account must not already be Closed or Frozen |
| `Unfreeze()` | Account must currently be Frozen |
| `Close()` | Balance must be exactly 0 |

**AccountNumber value object** — generated automatically on account open:
```csharp
public static AccountNumber Generate()
{
    var datePart = DateTime.UtcNow.ToString("yyyyMM");    // e.g., "202603"
    var randomPart = GenerateRandomChars(8);               // e.g., "0G7LMJ6G"
    return new AccountNumber($"ACC-{datePart}-{randomPart}");
}
```

Pattern validated on construction: `^ACC-\d{6}-[A-Z0-9]{8}$`

### Application: Read vs Write Models (CQRS)

**Commands** load the full `Account` aggregate from the repository:
```csharp
// DepositCommandHandler
var account = await _repository.GetByIdAsync(request.AccountId, ct);
account.Deposit(request.Amount, request.Reference);
await _repository.UpdateAsync(account, ct);
```

**Queries** bypass the aggregate entirely and project directly to DTOs using EF Core's `Select()`:
```csharp
// AccountReadRepository — no aggregate loading, just a SQL projection
return await _context.Accounts
    .Where(a => a.Id == id)
    .Select(a => new AccountDto(a.Id, a.OwnerId, a.AccountNumber, ...))
    .FirstOrDefaultAsync(ct);
```

This is more efficient for reads: no object graph to build, no domain logic to run, just a targeted SQL query that returns exactly what the API needs.

---

## 8. Infrastructure: Connecting Domain to the Database

### EF Core Entity Configurations

Instead of cluttering entity classes with EF attributes (`[MaxLength]`, `[Column]`), we use separate configuration classes:

```csharp
// AccountEntityConfiguration.cs
builder.ToTable("accounts");
builder.Property(a => a.BalanceAmount).HasColumnType("decimal(18,4)");
builder.Property(a => a.BalanceCurrency).HasMaxLength(3);
builder.Property(a => a.Version).IsConcurrencyToken();   // optimistic concurrency
builder.HasIndex(a => a.AccountNumber).IsUnique();
builder.HasIndex(a => a.OwnerId);                        // query performance
```

The `Version` concurrency token means: if two requests try to update the same account simultaneously, only the first one wins. The second sees that `Version` has changed and EF throws a `DbUpdateConcurrencyException`.

### Aggregate Reconstitution (Accounts)

Because `Account` has no public setters and a private constructor, EF Core cannot create and populate it directly. We use `RuntimeHelpers.GetUninitializedObject` — a low-level .NET method that creates an instance without calling any constructor.

```csharp
var account = (Account)RuntimeHelpers.GetUninitializedObject(typeof(Account));

// CRITICAL: initialize _domainEvents — GetUninitializedObject skips all field initializers
var domainEventsField = typeof(AggregateRoot)
    .GetField("_domainEvents", BindingFlags.NonPublic | BindingFlags.Instance);
domainEventsField?.SetValue(account, new List<DomainEvent>());

// Now set each property via reflection
SetProperty(account, "Id", entity.Id);
SetProperty(account, "Balance", new Money(entity.BalanceAmount, entity.BalanceCurrency));
// ... etc
```

**Why the `_domainEvents` initialization is critical:** `GetUninitializedObject` skips *all* constructors and field initializers. The field `private readonly List<DomainEvent> _domainEvents = new()` in `AggregateRoot` normally initializes in the constructor. When bypassed, `_domainEvents` is `null`. The first call to any domain method that raises an event will then crash with `NullReferenceException` when trying to `_domainEvents.Add(event)`.

### DbContext and Migrations

Each service has its own `DbContext` pointing to its own database:
- `IdentityDbContext` → `fincore_identity` → tables: `users`, `refresh_tokens`
- `AccountsDbContext` → `fincore_accounts` → tables: `accounts`

Migrations are code-generated snapshots of schema changes. To apply them:
```bash
dotnet ef database update \
  --project src/services/FinCore.Identity/FinCore.Identity.Infrastructure \
  --startup-project src/services/FinCore.Identity/FinCore.Identity.Api
```

The `--startup-project` flag is required because EF needs to load the startup project's DI container to find the `DbContext` registration and connection string.

### Dependency Injection Wiring

Each Infrastructure project exposes a single extension method that registers everything it owns:

```csharp
// Called from Program.cs:
builder.Services.AddIdentityInfrastructure(builder.Configuration);

// Inside AddIdentityInfrastructure:
services.AddDbContext<IdentityDbContext>(options => options.UseNpgsql(connectionString));
services.AddScoped<IUserRepository, EfUserRepository>();
services.AddScoped<IPasswordHasher, BcryptPasswordHasher>();
services.AddScoped<IJwtTokenService, JwtTokenService>();
services.Configure<JwtSettings>(options => { ... reads JWT env vars });
```

The Application layer only knows about `IUserRepository`. The Infrastructure layer provides the EF implementation. Swapping to a different database means only changing the Infrastructure project.

---

## 9. The API Layer: Exposing Behaviour Over HTTP

### Controllers

Controllers are thin. They:
1. Extract the current user's ID from the JWT (`sub` claim)
2. Build a Command or Query from the HTTP request body
3. Send it through MediatR
4. Map the `Result<T>` to an HTTP response

```csharp
[HttpPost("{id:guid}/deposit")]
public async Task<IActionResult> Deposit(Guid id, [FromBody] MoneyOperationRequest request, CancellationToken ct)
{
    var result = await _mediator.Send(new DepositCommand(id, request.Amount, request.Currency, request.Reference), ct);
    if (!result.IsSuccess)
        return BadRequest(new { error = result.Error });
    return Ok();
}
```

### Authorization

All Accounts endpoints require a valid JWT (`[Authorize]` on the controller). Role-based restrictions are applied per-endpoint:

```csharp
[HttpPost("{id:guid}/freeze")]
[Authorize(Roles = "ComplianceOfficer,Admin")]   // only these roles can freeze
public async Task<IActionResult> Freeze(...) { ... }
```

For `GET /accounts/{id}`, an attribute-based check is not enough because we need to verify ownership:
```csharp
var isPrivileged = User.IsInRole("Admin") || User.IsInRole("Analyst");
if (!isPrivileged && result.Value.OwnerId != currentUserId)
    return Forbid();    // customer can only see their own accounts
```

### Exception Handling Middleware

If something unexpected escapes to the API layer, `ExceptionHandlingMiddleware` catches it, logs it with the correlation ID, and returns a clean JSON error:

```csharp
catch (DomainException ex)
{
    context.Response.StatusCode = 400;
    await context.Response.WriteAsync(JsonSerializer.Serialize(
        new { error = ex.Message, correlationId }));
}
catch (Exception ex)
{
    _logger.LogError(ex, "Unhandled exception. CorrelationId: {CorrelationId}", correlationId);
    context.Response.StatusCode = 500;
    await context.Response.WriteAsync(JsonSerializer.Serialize(
        new { error = "An unexpected error occurred.", correlationId }));
}
```

The `correlationId` in every error response lets you trace the exact request in Seq logs.

### Scalar API Documentation

Both services expose interactive API documentation at `/scalar/v1` (in Development mode). This is a modern alternative to Swagger UI, powered by `Scalar.AspNetCore`.

---

## 10. Observability: Logging, Tracing, Health Checks

### Structured Logging with Serilog

Every log line includes consistent context fields:

```
[17:46:59 ERR] [accounts] 753bfd77-... Unhandled exception. CorrelationId: 753bfd77-...
```

Format: `[{Timestamp:HH:mm:ss} {Level}] [{ServiceName}] {CorrelationId} {Message}`

Logs go to two sinks simultaneously:
- **Console** — for local development
- **Seq** — a structured log aggregator running on `http://localhost:8080` (Docker). Seq lets you query logs with SQL-like syntax: `select * from stream where ServiceName = 'accounts' and @Level = 'Error'`

`SerilogConfiguration.cs` wires this up in one call:
```csharp
builder.Host.AddFinCoreLogging("accounts");
```

**Log levels:** Microsoft/EF Core logs are suppressed to `Warning` to avoid noise. Application code logs at `Information` and above.

### Correlation ID

Every HTTP request gets a correlation ID (from the `X-Correlation-Id` request header, or auto-generated). The `CorrelationIdMiddleware` pushes it into:
- The Serilog `LogContext` (so it appears in every log line for that request)
- `context.Items["CorrelationId"]` (so controllers and middleware can access it)
- The `X-Correlation-Id` response header (so the caller can match request to response)

This means you can paste a correlation ID into Seq and see every log line from that single request across all middleware, handlers, and repository calls.

### OpenTelemetry Tracing

`OpenTelemetryConfiguration.cs` instruments:
- All incoming ASP.NET Core HTTP requests
- All outbound HTTP calls (for future service-to-service calls)

In Development, traces are printed to the console. In Phase 2+, Jaeger will be added to visualise distributed traces spanning multiple services.

### Health Checks

Each service exposes two endpoints:

| Endpoint | Purpose | Passes when |
|---|---|---|
| `GET /health/live` | Is the process alive? | Always (no checks run) |
| `GET /health/ready` | Can it serve traffic? | PostgreSQL is reachable |

Used by container orchestrators (Docker, Kubernetes) to know when to route traffic to a service.

```csharp
app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = hc => hc.Tags.Contains("ready")   // only tagged checks
});
```

---

## 11. Docker Compose: Local Infrastructure

`infra/docker-compose.yml` starts all infrastructure services:

```yaml
services:
  postgres:
    image: postgres:16-alpine
    ports: ["5433:5432"]          # 5433 on host → avoids local Postgres conflict
    environment:
      POSTGRES_USER: fincore
      POSTGRES_PASSWORD: fincore_dev
    volumes:
      - postgres_data:/var/lib/postgresql/data
      - ../scripts/init-db.sh:/docker-entrypoint-initdb.d/init-db.sh:ro

  redis:
    image: redis:7-alpine
    ports: ["6379:6379"]

  kafka:
    image: confluentinc/cp-kafka:7.6.0    # KRaft mode — no ZooKeeper needed
    ports: ["9092:9092"]

  seq:
    image: datalust/seq:latest
    ports: ["5341:5341", "8080:80"]       # 8080 → Seq web UI
    environment:
      ACCEPT_EULA: "Y"
```

**Port 5433 instead of 5432:** The host machine has PostgreSQL 18 installed locally, occupying port 5432. Docker's Postgres is therefore mapped to 5433. All connection strings use port 5433.

**`scripts/init-db.sh`** runs automatically on the first Postgres start (Docker mounts it into `/docker-entrypoint-initdb.d/`):
```bash
CREATE DATABASE fincore_identity;
CREATE DATABASE fincore_accounts;
```

**`docker-compose.override.yml`** is gitignored and used for per-developer overrides (e.g., different ports on a machine where 5433 is also taken).

To start everything:
```bash
docker compose -f infra/docker-compose.yml up -d
```

---

## 12. Unit Tests: Proving the Domain Works

All 34 tests are in `tests/unit/` and test the domain layer only. No mocks, no databases — just pure C# objects.

**Why no mocks?** The domain layer has zero external dependencies, so there is nothing to mock. Tests create aggregates, call methods, and assert on properties and domain events.

### Example: Account tests

```csharp
[Fact]
public void Deposit_PositiveAmount_IncreasesBalance()
{
    var account = Account.Open(OwnerId, AccountType.Checking, "USD");
    account.ClearDomainEvents();

    account.Deposit(100m, "REF-001");

    account.Balance.Amount.Should().Be(100m);
    account.DomainEvents.Should().HaveCount(1);
    account.DomainEvents[0].Should().BeOfType<MoneyDeposited>();
}

[Fact]
public void Withdraw_InsufficientFunds_ThrowsInsufficientFundsException()
{
    var account = Account.Open(OwnerId, AccountType.Checking, "USD");
    account.Deposit(100m, "REF-001");

    var act = () => account.Withdraw(200m, "REF-002");

    act.Should().Throw<InsufficientFundsException>();
}
```

We use `FluentAssertions` for readable assertions (`Should().Be()`, `Should().Throw<>()`).

### Test coverage (Phase 1)

| Test class | Tests | What is covered |
|---|---|---|
| `AccountAggregateTests` | 8 | Open, Deposit, Withdraw, Freeze, Unfreeze, Close — both happy paths and business rule violations |
| `MoneyValueObjectTests` | 5 | Addition, subtraction, currency mismatch, value equality |
| `UserAggregateTests` | 10 | Register, Deactivate, AssignRole, duplicate email guard |
| `RefreshTokenTests` | 2 | Revoke, IsExpired |
| `EmailValueObjectTests` | 3 | Invalid format, case-insensitive equality |

**Running tests:**
```bash
dotnet test FinCore.slnx                                                   # all 34
dotnet test tests/unit/FinCore.Accounts.Domain.Tests                       # 16 tests
dotnet test tests/unit/FinCore.Accounts.Domain.Tests --filter "Money"      # just Money tests
```

---

## 13. Running Everything End-to-End

### Prerequisites

- .NET 10 SDK (`dotnet --version` should print `10.x.x`)
- Docker Desktop running
- `dotnet-ef` global tool: `dotnet tool install --global dotnet-ef`

### Step-by-step

**1. Start infrastructure:**
```bash
docker compose -f infra/docker-compose.yml up -d
```

Wait ~10 seconds for Postgres to initialise and run `init-db.sh`.

**2. Apply database migrations:**
```bash
dotnet ef database update \
  --project src/services/FinCore.Identity/FinCore.Identity.Infrastructure \
  --startup-project src/services/FinCore.Identity/FinCore.Identity.Api

dotnet ef database update \
  --project src/services/FinCore.Accounts/FinCore.Accounts.Infrastructure \
  --startup-project src/services/FinCore.Accounts/FinCore.Accounts.Api
```

**3. Run the services** (two terminal windows):
```bash
# Terminal 1
dotnet run --project src/services/FinCore.Identity/FinCore.Identity.Api

# Terminal 2
dotnet run --project src/services/FinCore.Accounts/FinCore.Accounts.Api
```

**4. Register and log in:**
```bash
# Register
curl -X POST http://localhost:5001/api/v1/auth/register \
  -H "Content-Type: application/json" \
  -d '{"email":"test@example.com","password":"MyPassword12345","role":"Admin"}'

# Login — copy the accessToken from the response
curl -X POST http://localhost:5001/api/v1/auth/login \
  -H "Content-Type: application/json" \
  -d '{"email":"test@example.com","password":"MyPassword12345"}'
```

**5. Open an account and make transactions:**
```bash
JWT="<paste accessToken here>"

# Open account
curl -X POST http://localhost:5002/api/v1/accounts \
  -H "Authorization: Bearer $JWT" \
  -H "Content-Type: application/json" \
  -d '{"accountType":"Checking","currency":"USD"}'

ACCOUNT_ID="<paste account id here>"

# Deposit
curl -X POST "http://localhost:5002/api/v1/accounts/$ACCOUNT_ID/deposit" \
  -H "Authorization: Bearer $JWT" \
  -H "Content-Type: application/json" \
  -d '{"amount":500,"currency":"USD","reference":"REF-001"}'

# Check balance
curl "http://localhost:5002/api/v1/accounts/$ACCOUNT_ID" \
  -H "Authorization: Bearer $JWT"
```

**6. Check health and logs:**
```bash
curl http://localhost:5001/health/ready    # → Healthy
curl http://localhost:5002/health/ready    # → Healthy
```

Open `http://localhost:8080` to view Seq — you can search all logs from both services in one place.

---

## 14. Bugs We Hit and How We Fixed Them

Building Phase 1 surfaced several real-world problems. Here is what happened and the reasoning behind each fix.

### Bug 1: .NET SDK version mismatch

**Problem:** The plan specified `net9.0` but the only SDK installed on the machine was .NET 10.0.103.

**Fix:** Updated `Directory.Build.props` to use `net10.0`. Also upgraded all package versions that had a net10 release.

**Lesson:** Always check `dotnet --version` before starting a project and set `TargetFramework` to match.

---

### Bug 2: Local PostgreSQL 18 intercepting Docker connections

**Problem:** `dotnet ef database update` failed with `password authentication failed for user "fincore"`. Even though Docker's Postgres was healthy, the local PostgreSQL 18 service (also on port 5432) answered the connection first — with different auth rules.

**Diagnosis:**
```
netstat output:
  TCP  0.0.0.0:5432  LISTENING  → two processes! Local PG18 AND Docker both bound.
```

**Fix:** Changed Docker's port mapping from `"5432:5432"` to `"5433:5432"` in `docker-compose.yml`. Updated all fallback connection strings to use port 5433.

**Lesson:** In development environments with multiple Postgres versions or other databases, always check for port conflicts. Using a non-standard port for Docker databases avoids surprises.

---

### Bug 3: `JwtSettings.Secret` was empty at runtime

**Problem:** `POST /api/v1/auth/login` returned 500. Log showed: `IDX10703: Cannot create a SymmetricSecurityKey, key length is zero.`

**Root cause:** `Program.cs` had a dev fallback for the JWT secret used in bearer validation, but `IdentityInfrastructureExtensions` read `JWT__SECRET` env var and defaulted to `string.Empty` when it was not set. So `JwtTokenService` got an empty key.

**Fix:** Added the dev fallback `"dev-secret-must-be-at-least-32-chars!!"` in `IdentityInfrastructureExtensions` so both code paths use the same default.

**Lesson:** When a secret is consumed in multiple places (bearer validation + token generation), both places need the same default. Review all reads of a secret when setting up dev defaults.

---

### Bug 4: `NullReferenceException` on first Deposit after account is loaded from DB

**Problem:** `POST /accounts/{id}/deposit` returned 500 after the account was saved. The first `OpenAccount` call worked, but the second operation on the same account failed.

**Log:** `NullReferenceException at ExceptionHandlingMiddleware.InvokeAsync line 23`

**Root cause:** `EfAccountRepository.MapToDomain` used `RuntimeHelpers.GetUninitializedObject(typeof(Account))` to create an `Account` instance without calling the constructor. This bypasses all field initializers, including `private readonly List<DomainEvent> _domainEvents = new()` in `AggregateRoot`. When the reconstituted account called `Deposit()` → `RaiseEvent()` → `_domainEvents.Add(event)`, it crashed because `_domainEvents` was `null`.

**Fix:** After calling `GetUninitializedObject`, initialize `_domainEvents` via reflection before setting any other properties:
```csharp
var domainEventsField = typeof(AggregateRoot)
    .GetField("_domainEvents", BindingFlags.NonPublic | BindingFlags.Instance);
domainEventsField?.SetValue(account, new List<DomainEvent>());
```

**Lesson:** `RuntimeHelpers.GetUninitializedObject` is powerful but dangerous — it skips *everything*, including field initializers. Any field with a `= new()` initializer in a base class will be null. Always initialize such fields manually when using this approach.

---

### Bug 5: `HashedPassword` violated the "zero external deps" rule for Domain

**Problem (caught in design):** The initial plan put `BCrypt.Hash()` inside `HashedPassword`'s constructor. But Domain can have zero external NuGet dependencies — BCrypt.Net-Next is an external package.

**Fix:** Made `HashedPassword` a pure string wrapper. Moved hashing to `IPasswordHasher` interface in Application, implemented by `BcryptPasswordHasher` in Infrastructure.

**Lesson:** The strict "Domain has zero external deps" rule exists so that the domain can be compiled and tested anywhere without dragging in database drivers, cryptography libraries, or anything else. Any time a domain class wants to use a NuGet package, extract an interface.

---

## 15. What Comes Next (Phase 2 Preview)

Phase 1 established the foundation. Phase 2 will add:

- **Kafka integration** — replace `NoOpEventBus` with a real Kafka producer/consumer using `Confluent.Kafka`. Domain events published to Kafka topics.
- **Outbox Pattern** — domain events written to an `OutboxMessages` table in the same DB transaction as the aggregate, then relayed to Kafka by a background `IHostedService`. This eliminates the dual-write problem (where saving to DB succeeds but Kafka publish fails, leaving systems inconsistent).
- **Integration tests** — `Testcontainers` to spin up real Postgres and Kafka in tests. No in-memory fakes.
- **Ledger service** — a new service using full Event Sourcing (state stored as a stream of events, no current-state table). Every financial transaction creates an immutable ledger entry.
- **gRPC** — internal service-to-service calls (e.g., Accounts verifying user existence with Identity) over gRPC instead of HTTP.
- **YARP API Gateway** — a reverse proxy that routes external requests to the correct microservice.

The architecture is intentionally designed so Phase 2 additions slot in without changing the Domain or Application layers.
