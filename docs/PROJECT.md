# FinCore — C# .NET Backend Pet Project

A comprehensive backend system simulating a **financial data platform** — designed to practice foundational and advanced architecture patterns found in real-world finance and banking systems.

---

## Table of Contents

1. [Project Vision](#1-project-vision)
2. [Domain Overview](#2-domain-overview)
3. [Foundational Architecture](#3-foundational-architecture)
4. [Microservices Decomposition](#4-microservices-decomposition)
5. [Data Architecture & Data Lake](#5-data-architecture--data-lake)
6. [Big Data Patterns](#6-big-data-patterns)
7. [Event-Driven Architecture](#7-event-driven-architecture)
8. [Security & Compliance (Finance/Banking)](#8-security--compliance-financebanking)
9. [Distributed Systems Patterns](#9-distributed-systems-patterns)
10. [Observability & Reliability](#10-observability--reliability)
11. [Testing Strategy](#11-testing-strategy)
12. [Technology Stack](#12-technology-stack)
13. [Project Structure](#13-project-structure)
14. [Implementation Roadmap](#14-implementation-roadmap)

---

## 1. Project Vision

**FinCore** is a simulated financial backend platform that handles:

- Customer accounts, transactions, and portfolios
- Real-time fraud detection pipelines
- Regulatory reporting and audit trails
- Multi-zone data lake with hot/warm/cold tiers
- Event-sourced ledger for immutable financial records

The goal is not to ship a product but to **practice the patterns** used in enterprise-grade, highly regulated, data-intensive systems.

---

## 2. Domain Overview

### Core Domains (DDD Bounded Contexts)

```
┌─────────────────────────────────────────────────────────┐
│                        FinCore                          │
├──────────────┬──────────────┬──────────────┬────────────┤
│   Identity   │   Accounts   │  Transactions│  Reporting │
│   & Auth     │  & Wallets   │  & Ledger    │  & Audit   │
├──────────────┼──────────────┼──────────────┼────────────┤
│   Fraud      │  Compliance  │  Notifications│  Analytics │
│   Detection  │  & KYC       │  & Alerts    │  & ML Feed │
└──────────────┴──────────────┴──────────────┴────────────┘
```

### Key Entities

| Entity | Domain | Notes |
|---|---|---|
| Customer | Identity | PII, encrypted at rest |
| Account | Accounts | Multi-currency, multi-type |
| Transaction | Ledger | Immutable, event-sourced |
| AuditEntry | Compliance | Append-only, tamper-evident |
| FraudSignal | Fraud | Stream-processed |
| KYCDocument | Compliance | Sensitive, tiered access |
| Report | Reporting | Regulatory (PCI, AML, GDPR) |

---

## 3. Foundational Architecture

### 3.1 Clean Architecture (per service)

Each microservice follows Clean Architecture layers:

```
┌─────────────────────────────────┐
│          Presentation           │  Controllers, gRPC handlers, Consumers
├─────────────────────────────────┤
│          Application            │  Use cases, Commands, Queries, DTOs
├─────────────────────────────────┤
│            Domain               │  Entities, Aggregates, Domain Events, Value Objects
├─────────────────────────────────┤
│         Infrastructure          │  EF Core, Kafka, Redis, S3, external APIs
└─────────────────────────────────┘
```

**Rules:**
- Domain has zero external dependencies
- Application depends only on Domain
- Infrastructure implements interfaces defined in Application/Domain
- Dependency Inversion enforced via DI container

### 3.2 Domain-Driven Design (DDD)

Key DDD building blocks used throughout:

- **Aggregates** — Transaction, Account (enforce invariants, single transactional boundary)
- **Value Objects** — Money (amount + currency), IBAN, TransactionId (immutable, equality by value)
- **Domain Events** — `TransactionCreated`, `FraudFlagged`, `AccountFrozen`
- **Repositories** — one per aggregate root, abstracted behind interfaces
- **Domain Services** — cross-aggregate logic (e.g., `FundTransferService`)
- **Bounded Contexts** — each microservice owns its context; shared kernel for cross-cutting types

### 3.3 CQRS (Command Query Responsibility Segregation)

Separate read and write models per domain:

```
Write Side                        Read Side
──────────────────────────────────────────────────────
Command → CommandHandler          Query → QueryHandler
       → Domain Logic                  → Read Model (denormalized)
       → Aggregate.Apply(event)        → Projection / View Store
       → EventStore.Append             → (Redis, ReadDB, Elasticsearch)
```

- Commands mutate state via aggregates
- Queries hit optimized read projections (never the write store)
- Eventual consistency between write and read sides via event projections

### 3.4 Event Sourcing

The Ledger domain uses full Event Sourcing:

- State is derived entirely from a sequence of immutable domain events
- `EventStore` table: `(StreamId, Version, EventType, Payload, Timestamp, CorrelationId)`
- Snapshots every N events for performance
- Replay capability for rebuilding projections or debugging

```csharp
// Example aggregate pattern
public class Account : AggregateRoot
{
    public Money Balance { get; private set; }

    public void Debit(Money amount)
    {
        if (amount > Balance) throw new InsufficientFundsException();
        RaiseEvent(new AccountDebited(Id, amount, DateTime.UtcNow));
    }

    private void Apply(AccountDebited e)
    {
        Balance -= e.Amount;
    }
}
```

### 3.5 Hexagonal Architecture (Ports & Adapters)

Applied at the service boundary level:

- **Ports** = interfaces defined in Application layer (IEventBus, IAccountRepository, IEmailSender)
- **Adapters** = concrete implementations in Infrastructure (KafkaEventBus, EfAccountRepository, SendGridEmailSender)
- Enables swapping infrastructure without touching business logic
- Essential for testability (swap real adapters with fakes in tests)

---

## 4. Microservices Decomposition

```
                          ┌──────────────┐
                          │  API Gateway │  (YARP / Ocelot)
                          └──────┬───────┘
           ┌──────────────┬──────┴──────┬──────────────┐
           │              │             │               │
    ┌──────▼──────┐ ┌─────▼──────┐ ┌───▼──────┐ ┌─────▼──────┐
    │  Identity   │ │  Accounts  │ │  Ledger  │ │  Reporting │
    │  Service    │ │  Service   │ │  Service │ │  Service   │
    └─────────────┘ └────────────┘ └──────────┘ └────────────┘
           │              │             │               │
    ┌──────▼──────┐ ┌─────▼──────┐ ┌───▼──────┐ ┌─────▼──────┐
    │   Fraud     │ │ Compliance │ │Notif.    │ │ Analytics  │
    │  Detection  │ │  / KYC     │ │Service   │ │  Service   │
    └─────────────┘ └────────────┘ └──────────┘ └────────────┘
           │              │             │               │
           └──────────────┴──────┬──────┴───────────────┘
                                 │
                        ┌────────▼────────┐
                        │   Message Bus   │  (Kafka)
                        └─────────────────┘
```

### Service Responsibilities

| Service | Pattern | DB | Notes |
|---|---|---|---|
| Identity | Clean Arch + JWT | PostgreSQL | BCrypt passwords, refresh tokens |
| Accounts | DDD + CQRS | PostgreSQL + Redis | Account lifecycle, balances |
| Ledger | Event Sourcing | EventStore / PostgreSQL | Immutable transaction log |
| Fraud Detection | Stream processing | Redis + TimescaleDB | Real-time signals |
| Compliance/KYC | Clean Arch | PostgreSQL + S3 | Document vault |
| Reporting | CQRS read side | Elasticsearch | Regulatory, audit queries |
| Analytics | Lambda Architecture | Data Lake (S3/Parquet) | Batch + stream |
| Notifications | Worker Service | PostgreSQL + Redis | Outbox pattern |

---

## 5. Data Architecture & Data Lake

### 5.1 Data Zones (Medallion Architecture)

```
Raw Events / External Feeds
         │
         ▼
┌─────────────────┐
│   Bronze Zone   │  Raw, unprocessed data — S3/MinIO, Parquet
│  (Landing Zone) │  Immutable append-only; schema-on-read
└────────┬────────┘
         │  Cleanse + Validate
         ▼
┌─────────────────┐
│   Silver Zone   │  Cleaned, typed, deduplicated — Parquet + schema registry
│  (Curated)      │  Joins across domains, PII masked
└────────┬────────┘
         │  Aggregate + Enrich
         ▼
┌─────────────────┐
│   Gold Zone     │  Business-ready datasets — optimized for queries
│  (Consumption)  │  Regulatory reports, ML features, dashboards
└─────────────────┘
```

### 5.2 Data Types & Stores

| Data Type | Store | Zone | Retention |
|---|---|---|---|
| Transaction events | Kafka → EventStore | Bronze | Permanent |
| Customer PII | PostgreSQL (encrypted) | Silver | GDPR-compliant |
| Audit logs | Append-only table + S3 | Bronze/Silver | 7 years (regulatory) |
| Fraud signals | TimescaleDB | Silver | 2 years |
| Behavioral analytics | Parquet on S3 | Gold | Aggregated |
| ML training data | Parquet + Delta Lake | Gold | Versioned |
| Documents (KYC) | S3 + metadata in PG | Bronze | Encrypted, 10 years |
| Read projections | Redis + Elasticsearch | Gold | Derived, rebuildable |

### 5.3 Hot / Warm / Cold Storage Tiers

```
HOT   → Redis cache (ms latency)        Account balances, session tokens
      → PostgreSQL primary              Active transactions, last 90 days

WARM  → PostgreSQL read replicas        Queries on last 1–2 years
      → Elasticsearch                   Full-text, audit search

COLD  → S3 / MinIO (Parquet/ORC)        Archive, raw events, regulatory data
      → Glacier / compressed            7-year retention blobs
```

---

## 6. Big Data Patterns

### 6.1 Lambda Architecture

```
Input Stream (Kafka)
       │
       ├──► Speed Layer  ──► Real-time views (Redis, TimescaleDB)
       │     (Kafka Streams / Flink analogue in .NET)
       │
       └──► Batch Layer  ──► Batch views (S3 Parquet → Gold Zone)
              (Scheduled jobs, Hangfire / .NET Worker)
                    │
                    └──► Serving Layer ──► Merged query results
```

- Speed layer handles fraud signals, real-time balance checks
- Batch layer handles regulatory aggregations, daily reconciliation
- Serving layer merges both for consistent reads

### 6.2 Kappa Architecture (for Ledger)

- All processing via Kafka event streams
- Reprocessing by replaying from beginning of topic
- Simpler than Lambda — single code path for batch and stream
- Applied to the Ledger service (event-sourced + stream projections)

### 6.3 Batch Processing Patterns

- **ETL Pipeline** — Extract (Kafka/DB) → Transform (normalise, mask PII) → Load (Gold Zone)
- **Reconciliation Jobs** — daily balance reconciliation across accounts
- **Report Generation** — scheduled AML, regulatory reports via Worker Services
- **Data Compaction** — merge small Parquet files, apply Delta Lake vacuum

### 6.4 Stream Processing Patterns

- **Windowed Aggregations** — fraud: count transactions per customer per 5-min window
- **Event Enrichment** — join raw transaction events with customer profile on the fly
- **Dead Letter Queue (DLQ)** — failed messages routed to DLQ topic for reprocessing
- **Outbox Pattern** — guaranteed at-least-once delivery from DB to Kafka

---

## 7. Event-Driven Architecture

### 7.1 Event Types

| Type | Example | Guarantee |
|---|---|---|
| Domain Event | `AccountDebited` | At-least-once, idempotent |
| Integration Event | `TransactionSettled` | Published after DB commit |
| Notification Event | `FraudAlertRaised` | Fire-and-forget |
| Audit Event | `UserLoggedIn` | Append-only, never dropped |

### 7.2 Patterns

**Outbox Pattern** — prevents dual-write inconsistency:
```
1. Write domain event to `OutboxMessages` table in same DB transaction
2. Outbox relay (background worker) polls and publishes to Kafka
3. Mark as published only after Kafka ACK
```

**Saga Pattern** — manages distributed transactions:
```
FundTransfer Saga:
  Step 1: Reserve funds (Accounts Service)     → CompensateStep1: Release hold
  Step 2: Record debit (Ledger Service)        → CompensateStep2: Reverse entry
  Step 3: Credit destination (Accounts Service) → CompensateStep3: Reverse credit
  Step 4: Confirm transfer (Notifications)     → no compensation needed
```

Choreography-based (events trigger next step) vs Orchestration-based (central saga coordinator) — implement both variants.

**Event Versioning** — schema evolution without breaking consumers:
- Additive changes only (new optional fields)
- Version field on every event envelope
- Upcasters in event store to migrate old events on read

### 7.3 Message Bus (Kafka Topics)

```
fincore.transactions.created
fincore.transactions.settled
fincore.accounts.frozen
fincore.fraud.signal
fincore.fraud.alert
fincore.compliance.kyc-approved
fincore.audit.log
fincore.notifications.send
fincore.dlq.*
```

---

## 8. Security & Compliance (Finance/Banking)

### 8.1 Authentication & Authorization

- **JWT + Refresh Tokens** — short-lived access tokens (15 min), long-lived refresh (7 days, rotated)
- **Role-Based Access Control (RBAC)** — roles: `customer`, `analyst`, `compliance-officer`, `admin`
- **Attribute-Based Access Control (ABAC)** — fine-grained: customer can only access their own data
- **MFA simulation** — TOTP-based second factor
- **API Key management** — for service-to-service auth (internal services)

### 8.2 Data Encryption

| Layer | Method |
|---|---|
| In transit | TLS 1.3 everywhere (service mesh / Kestrel) |
| At rest (DB) | PostgreSQL TDE or column-level encryption (EF Core value converters) |
| At rest (files) | S3 server-side encryption (AES-256) |
| Application-level | AES-256-GCM for PII fields (names, SSN, card numbers) |
| Keys | Envelope encryption via HashiCorp Vault / Azure Key Vault |

### 8.3 PII Handling

- Field-level encryption on Customer entity (name, email, phone, address, SSN)
- Data masking in logs and analytics (card numbers → `****-****-****-1234`)
- GDPR: right to erasure via crypto-shredding (delete encryption key, data becomes unreadable)
- Separation: PII in operational DB, anonymized in analytics Data Lake

### 8.4 Audit Trail

- Every state change appended to `AuditLog` table — never updated, never deleted
- Fields: `EntityType`, `EntityId`, `Action`, `ActorId`, `Timestamp`, `OldValue (encrypted)`, `NewValue (encrypted)`, `CorrelationId`
- Tamper-evident: hash chain (each entry includes hash of previous entry)
- Long-term: audit logs streamed to cold S3 with 7-year retention

### 8.5 Compliance Patterns

- **PCI-DSS** — no raw card data stored; tokenization; network segmentation
- **AML (Anti-Money Laundering)** — transaction monitoring rules engine; CTR/SAR report generation
- **KYC (Know Your Customer)** — document upload, verification states, expiry tracking
- **SOX / regulatory reporting** — immutable financial reports generated and signed
- **Data residency** — tenant-aware routing for multi-region compliance

### 8.6 Secret Management

- No secrets in code or config files
- All secrets via environment variables injected at runtime or pulled from Vault
- Secret rotation without service restart (Vault dynamic secrets)
- Separate secrets per environment (dev / staging / prod)

---

## 9. Distributed Systems Patterns

### 9.1 Resilience Patterns

| Pattern | Library | Use Case |
|---|---|---|
| Retry with exponential backoff | Polly | External API calls, DB transient errors |
| Circuit Breaker | Polly | Payment gateway, fraud API |
| Timeout | Polly / HttpClient | All outbound calls |
| Bulkhead | Polly | Isolate thread pools per downstream |
| Fallback | Polly | Serve cached data when upstream fails |

### 9.2 Consistency Patterns

- **Optimistic Concurrency** — version field on aggregates, detect conflicts at save
- **Idempotency Keys** — clients send unique key; server deduplicates replayed requests
- **Two-Phase Commit (2PC)** — avoided; replaced by Saga pattern
- **Eventual Consistency** — read models updated asynchronously via events; documented per endpoint

### 9.3 API Design Patterns

- **API Gateway** — YARP reverse proxy; routing, rate limiting, auth validation, request aggregation
- **BFF (Backend for Frontend)** — tailored APIs per client type (mobile, web, internal)
- **Versioning** — URL versioning (`/api/v1/`, `/api/v2/`)
- **Pagination** — cursor-based for large result sets (not offset-based)
- **HATEOAS** — hypermedia links in responses for discoverability (optional advanced)

### 9.4 Caching Patterns

- **Cache-Aside** — application manages cache (read from Redis, fallback to DB, populate cache)
- **Write-Through** — write to cache and DB simultaneously (account balance)
- **Cache Invalidation** — event-driven invalidation (AccountUpdated → invalidate cache key)
- **Distributed Lock** — Redlock algorithm for preventing race conditions on critical sections

### 9.5 Service Discovery & Communication

- **Synchronous** — REST (public API) and gRPC (internal service-to-service)
- **Asynchronous** — Kafka for events and commands that cross service boundaries
- **Service Mesh (simulated)** — YARP + custom middleware for routing, retries, tracing headers

---

## 10. Observability & Reliability

### 10.1 The Three Pillars

**Structured Logging (Serilog)**
```json
{
  "timestamp": "2025-01-01T10:00:00Z",
  "level": "Warning",
  "message": "Fraud signal detected",
  "correlationId": "abc-123",
  "customerId": "cust-456",
  "transactionId": "tx-789",
  "riskScore": 87.5
}
```

**Distributed Tracing (OpenTelemetry)**
- Trace ID propagated across all services via HTTP headers and Kafka message headers
- Spans for DB queries, Kafka produce/consume, HTTP calls
- Export to Jaeger or Zipkin

**Metrics (OpenTelemetry + Prometheus)**
- `transactions_total` (counter, by type, by status)
- `transaction_amount_histogram` (distribution of amounts)
- `fraud_signals_per_minute` (gauge)
- `http_request_duration_seconds` (histogram, by endpoint)
- `db_query_duration_seconds` (histogram, by query type)

### 10.2 Health Checks

- `/health/live` — is the process alive?
- `/health/ready` — are dependencies (DB, Kafka, Redis) reachable?
- Registered via `IHealthCheck` interface, exposed via ASP.NET Core health middleware

### 10.3 Reliability

- **Graceful shutdown** — drain in-flight requests before stopping
- **Idempotent consumers** — Kafka consumers safe to replay
- **Exactly-once semantics** — Kafka transactional producer for critical paths
- **SLO tracking** — 99.9% availability target modeled in metrics dashboards

---

## 11. Testing Strategy

### Pyramid

```
        ┌───────┐
        │  E2E  │   Few, slow, full flow tests
        ├───────┤
        │ Integ │   Per service, real DB (Testcontainers), no mocks for infra
        ├───────┤
        │ Unit  │   Many, fast — domain logic, use cases, pure functions
        └───────┘
```

### Patterns Per Layer

| Layer | Tool | Approach |
|---|---|---|
| Domain | xUnit | No mocks — pure logic, test aggregates directly |
| Application | xUnit + Moq | Mock repositories and external ports |
| Infrastructure | xUnit + Testcontainers | Real PostgreSQL / Redis / Kafka in Docker |
| API | WebApplicationFactory | Test HTTP stack end-to-end per service |
| Contract | Pact.NET | Consumer-driven contract tests between services |

### Key Test Types

- **Aggregate tests** — given events → apply command → assert new events raised
- **Saga tests** — simulate step failures, assert compensation triggered
- **Projection tests** — replay events → assert read model state
- **Security tests** — assert unauthorized access returns 403, PII not leaked in responses
- **Performance tests** — k6 or NBomber for throughput and latency baselines

---

## 12. Technology Stack

| Concern | Technology |
|---|---|
| Runtime | .NET 9, C# 13 |
| Web framework | ASP.NET Core Web API |
| gRPC | Grpc.AspNetCore |
| ORM | Entity Framework Core 9 |
| Migrations | EF Core Migrations |
| Event Bus | Apache Kafka (Confluent.Kafka) |
| Cache | Redis (StackExchange.Redis) |
| Primary DB | PostgreSQL |
| Time-series DB | TimescaleDB (PostgreSQL extension) |
| Search | Elasticsearch (NEST / Elastic.Clients.Elasticsearch) |
| Object Storage | MinIO (S3-compatible, local dev) |
| Secret Management | HashiCorp Vault (VaultSharp) |
| API Gateway | YARP (Microsoft) |
| Resilience | Polly v8 |
| Logging | Serilog + Seq (local) |
| Tracing | OpenTelemetry .NET SDK → Jaeger |
| Metrics | OpenTelemetry → Prometheus + Grafana |
| Health checks | AspNetCore.Diagnostics.HealthChecks |
| Background jobs | Hangfire (batch) + .NET Worker Service |
| Testing | xUnit, Moq, Testcontainers, Bogus (fake data) |
| Containerization | Docker + Docker Compose |
| API Docs | Scalar / Swashbuckle |
| Validation | FluentValidation |
| Mapping | Mapperly (source-gen based) |

---

## 13. Project Structure

```
FinCore/
├── src/
│   ├── services/
│   │   ├── FinCore.Identity/
│   │   │   ├── FinCore.Identity.Api/
│   │   │   ├── FinCore.Identity.Application/
│   │   │   ├── FinCore.Identity.Domain/
│   │   │   └── FinCore.Identity.Infrastructure/
│   │   ├── FinCore.Accounts/
│   │   │   ├── FinCore.Accounts.Api/
│   │   │   ├── FinCore.Accounts.Application/
│   │   │   ├── FinCore.Accounts.Domain/
│   │   │   └── FinCore.Accounts.Infrastructure/
│   │   ├── FinCore.Ledger/              # Event-sourced
│   │   ├── FinCore.FraudDetection/      # Stream processing
│   │   ├── FinCore.Compliance/          # KYC, AML
│   │   ├── FinCore.Reporting/           # CQRS read side
│   │   ├── FinCore.Analytics/           # Lambda/Kappa
│   │   └── FinCore.Notifications/       # Outbox + worker
│   ├── gateways/
│   │   └── FinCore.ApiGateway/          # YARP
│   └── shared/
│       ├── FinCore.SharedKernel/        # Value objects, base classes, interfaces
│       ├── FinCore.EventBus.Abstractions/
│       ├── FinCore.EventBus.Kafka/
│       └── FinCore.Observability/       # OTel setup, logging conventions
├── tests/
│   ├── unit/
│   ├── integration/
│   └── contracts/
├── infra/
│   ├── docker-compose.yml               # Kafka, PG, Redis, MinIO, Vault, Jaeger
│   ├── docker-compose.override.yml
│   └── k8s/                             # Kubernetes manifests (optional)
├── docs/
│   ├── architecture/
│   │   ├── adr/                         # Architecture Decision Records
│   │   └── diagrams/
│   └── api/
├── scripts/
│   ├── seed-data.sh
│   └── run-tests.sh
├── FinCore.sln
└── PROJECT.md
```

---

## 14. Implementation Roadmap

### Phase 1 — Foundation
- [ ] Solution structure, shared kernel, base classes
- [ ] Identity service (JWT auth, RBAC)
- [ ] Accounts service (DDD aggregates, EF Core, PostgreSQL)
- [ ] Docker Compose for infrastructure (PG, Redis, Kafka)
- [ ] Structured logging + health checks

### Phase 2 — Event-Driven Core
- [ ] Kafka integration (producer/consumer abstractions)
- [ ] Outbox pattern implementation
- [ ] Ledger service (event sourcing, EventStore)
- [ ] CQRS read side with Redis projections
- [ ] Distributed tracing (OpenTelemetry → Jaeger)

### Phase 3 — Data Architecture
- [ ] MinIO setup + file upload/download
- [ ] Bronze/Silver/Gold zone pipeline
- [ ] TimescaleDB for time-series (fraud signals)
- [ ] Elasticsearch for audit search
- [ ] Batch processing with Hangfire

### Phase 4 — Resilience & Security
- [ ] Polly policies (retry, circuit breaker, bulkhead)
- [ ] Field-level PII encryption (EF value converters + AES-256)
- [ ] HashiCorp Vault integration (secret management)
- [ ] Idempotency key middleware
- [ ] Audit log with hash chain

### Phase 5 — Advanced Patterns
- [ ] Saga orchestration (FundTransfer saga)
- [ ] Fraud detection stream processor
- [ ] API Gateway (YARP + rate limiting)
- [ ] Contract tests (Pact.NET)
- [ ] KYC workflow (state machine with Stateless or custom)

### Phase 6 — Observability & Hardening
- [ ] Prometheus metrics + Grafana dashboards
- [ ] Performance tests (NBomber)
- [ ] GDPR crypto-shredding implementation
- [ ] Kubernetes manifests (optional)
- [ ] Architecture Decision Records (ADRs) written up

---

## Architecture Decision Records (ADRs)

Short decisions to document as you build:

| # | Decision | Options Considered | Chosen |
|---|---|---|---|
| 001 | Event store implementation | MartenDB, custom PG table, EventStoreDB | Custom PG (learning value) |
| 002 | Service communication | REST only, gRPC only, REST+gRPC | REST (external) + gRPC (internal) |
| 003 | Saga style | Choreography, Orchestration | Orchestration (clearer for finance) |
| 004 | Data lake storage | Azure Data Lake, AWS S3, MinIO | MinIO (local, S3-compatible) |
| 005 | PII encryption | DB-level TDE, column-level, app-level | App-level (portable, testable) |
| 006 | Secret management | ENV vars only, Vault, Azure Key Vault | Vault (most realistic) |

---

*This document is the living specification for the FinCore pet project. Update as architectural decisions evolve.*
