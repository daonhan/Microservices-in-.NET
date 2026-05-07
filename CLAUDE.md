# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Entry points

- [README.md](README.md) — full architecture diagram, services table, deploy guide.
- [CONTEXT.md](CONTEXT.md) — human-narrated project pitch, decisions index, ADR links.
- [.claude/CLAUDE.md](.claude/CLAUDE.md) — behavioral guidelines (think first, simplicity, surgical changes).

## Repo shape

.NET microservices monorepo. Each top-level `*-microservice/` (and `api-gateway/`, `shared-libs/`) is an independent solution using a `.slnx` file (no root `.sln`). All projects target **net10.0**.

Services and ports (see `docker-compose.yaml`):

| Service | Port | Datastore |
|---|---|---|
| basket | 8000 | Redis |
| order | 8001 | SQL Server (+ Redis cache) |
| product | 8002 | SQL Server |
| auth | 8003 | SQL Server |
| api-gateway | 8004 | — (YARP, Ocelot fallback via `Gateway:Provider`) |
| inventory | 8005 | SQL Server |
| shipping | 8006 | SQL Server |
| payment | 8007 | SQL Server |

## Build / test / run

Operate per-solution from the service directory — there is no root solution.

```bash
# Build a service (restore happens implicitly)
cd order-microservice && dotnet build

# Test a service
cd order-microservice && dotnet test
cd order-microservice && dotnet test --filter "FullyQualifiedName~OrderEndpointTests"   # single class
cd order-microservice && dotnet test --filter "DisplayName~Given_X_When_Y_Then_Z"        # single test

# Format check (mirrors pre-commit)
dotnet format --verify-no-changes --verbosity minimal
dotnet format                                                 # apply fixes

# Full stack via Docker
docker compose up --build
docker compose up sql rabbitmq redis -d                       # infra only, then dotnet run a service
```

`Directory.Build.props` enables `TreatWarningsAsErrors` and `EnforceCodeStyleInBuild` — analyzer warnings break the build. The `NoWarn` list there documents intentional exemptions (e.g. `CA1707` for `Given_When_Then` test names, `CA1711` for `*EventHandler` types).

## Pre-commit (Husky.Net)

One-time activation per fresh clone:

```bash
dotnet tool restore
dotnet husky install
```

`.husky/task-runner.json` runs on commit:
1. `dotnet format --verify-no-changes`
2. `dotnet build --no-restore`
3. `dotnet test basket-microservice/Basket.Service.slnx --no-build --no-restore`

Only Basket tests run pre-commit. Run other service test suites manually before pushing changes that cross service boundaries.

Known environment issue (do not bypass hooks): in some WSL/virtiofs sandboxes, pre-commit can fail at `dotnet build --no-restore` with `MSB3248` (`No such device`) when test-project references are read from root-owned `bin/obj` artifacts. Treat this as filesystem ownership/mount behavior, not a branch regression.

Workaround path:

```bash
# run from a writable host shell
find . -type d \( -name bin -o -name obj \) -prune -exec rm -rf {} +
dotnet restore
dotnet husky run --group pre-commit
```

If ownership restrictions prevent cleanup in the current sandbox, finish the commit on a host where hooks pass (Windows PowerShell from the checkout path, or a WSL-native checkout under `~/src`).

## Shared library workflow (`ECommerce.Shared`)

`shared-libs/ECommerce.Shared` is consumed as a **NuGet package** (`<PackageReference Include="ECommerce.Shared" Version="..." />`), not a project reference. Current published version lives in `shared-libs/ECommerce.Shared/ECommerce.Shared.csproj`. The package is published to `local-nuget-packages/` (gitignored). After editing the shared lib:

```bash
cd shared-libs/ECommerce.Shared
dotnet pack -c Release
dotnet nuget push bin/Release/*.nupkg -s ../../local-nuget-packages
# Bump <Version> in ECommerce.Shared.csproj if consumers should pick it up
```

Consumers won't see changes until the version is bumped and the new `.nupkg` lands in the local feed.

## Cross-service architecture

The "big picture" lives in three places that have to be read together:

1. **Each service's `Program.cs`** — composition root. All wiring uses extension methods from `ECommerce.Shared`: `AddSqlServerDatastore`, `AddOutbox`, `AddRabbitMqEventBus`, `AddRabbitMqEventPublisher`, `AddRabbitMqSubscriberService`, `AddEventHandler<TEvent, THandler>`, `AddPlatformObservability`, `AddPlatformHealthChecks`, `AddPlatformOpenApi`. New cross-cutting concerns belong in `shared-libs/ECommerce.Shared`, not duplicated per service.

2. **`shared-libs/ECommerce.Shared/Infrastructure/`** —
   - `EventBus/` — `IEventBus`, `Event` base type, handler registration via keyed DI.
   - `RabbitMq/` — fanout exchange `ecommerce-exchange`, `RabbitMqHostedService` subscribes, `RabbitMqEventBus` publishes, OTEL context propagates through message headers (`RabbitMqTelemetry`).
   - `Outbox/` — transactional outbox. `OutboxBackgroundService` polls `OutboxContext` for unpublished events. Services that publish events must call `AddOutbox(...)` and (in Development) `app.ApplyOutboxMigrations()`.

3. **Saga across Order, Inventory, Payment, Shipping** — `OrderCreatedEvent` → Inventory reserves stock → `StockReserved`/`StockReservationFailed` → Order emits `OrderConfirmed`/`OrderCancelled` → Inventory commits or releases → Payment authorizes/captures (`PaymentAuthorized`/`PaymentCaptured`/`PaymentFailed`/`PaymentRefunded`) → Shipping creates a shipment on `StockCommitted` (`ShipmentCreated`/`ShipmentDispatched`/`ShipmentDelivered`/`ShipmentCancelled`/`ShipmentReturned`/`ShipmentFailed`). No central orchestrator — each service reacts to upstream events. Touching one leg without considering the others desynchronizes the flow. Event types live in each service's `IntegrationEvents/Events/`; handlers in `IntegrationEvents/EventHandlers/`.

Each service follows the same internal layout: `Endpoints/` (Minimal API handlers), `ApiModels/` (DTOs), `Models/` (domain), `Infrastructure/Data/` (EF Core or Redis), `IntegrationEvents/`, `Migrations/`. Keep this split — DTOs in `ApiModels`, domain types in `Models`.

## API Gateway provider switch

The gateway compiles **both** YARP and Ocelot. `Gateway:Provider` (env `Gateway__Provider`) selects at startup; values `Yarp` (default) or `Ocelot`. Unknown values fail fast. Logged at boot as `ApiGateway starting with provider=...`. Routes, port, auth, health checks, and metrics are identical across both — no client-side change needed when switching.

## DLQ + operator API

Messages that exhaust their retry budget on a consumer queue are dead-lettered to the platform-wide `ecommerce-dlq` exchange. A poller in the API Gateway persists them — plus failed outbox rows pulled from each service's `/internal/outbox/failed` (gated by `RequireService`) — into the gateway-owned `dead_letter_messages` table. Operators interact via gateway endpoints under `/operator/api/failures*` (Bearer + `Operator` claim): list/detail, single replay, batch replay, discard. Replay re-publishes to the failure's `OriginalQueue`; `dlq.replay` spans carry the original `CorrelationId` for trace linking. Prometheus counters: `dlq_messages_total`, `dlq_replays_total`, `dlq_discards_total`.

When changing event payloads, retry policy, or queue routing, audit both the consumer side **and** the DLQ poller — failures here surface as "stuck" messages users can't replay.

## Authentication

RS256 user JWTs from Auth (`POST /login`) and `client_credentials` service tokens (`POST /token`). Resource services validate via `AddJwtAuthentication()` which fetches+caches Auth's `/jwks` — **no shared symmetric secret**. Service-to-service endpoints under `/internal/*` are gated by the shared `RequireService` policy (requires `scope=service`); user tokens cannot reach them. Dev RSA keys ship under `auth-microservice/Auth.Service/dev-keys/`.

## CI/CD

**Azure Pipelines per service** (`azure-pipelines.yml` in each service dir). Bicep provisions cloud infra (AKS, ACR, Azure SQL, Service Bus, App Insights). Deploy stages roll images into `ecommerce-dev`/`-staging`/`-prod` AKS namespaces. GitHub Actions is **not** used.

## Conventions worth knowing

- File-scoped namespaces, `var` preferred, usings outside namespace (enforced by `.editorconfig`, warning level).
- Test names use `Given_When_Then` with underscores (suppressed `CA1707`).
- EF Core migrations under `**/Migrations/*.cs` are marked `generated_code = true` — don't hand-edit style.
- `IDesignTimeDbContextFactory` is implemented per service so `dotnet ef migrations add ...` works without running `Program.cs`.
- Integration tests use `WebApplicationFactory<Program>`; each service exposes `public partial class Program { }` at the bottom of `Program.cs` to make this work.

## What this repo does **not** use

To prevent plausible-but-wrong suggestions:

- **Libraries:** MediatR, AutoMapper, FluentValidation, Scrutor, Serilog. (Polly **is** used — RabbitMQ retry pipelines + EF Core retries.)
- **Web style:** MVC controllers (Minimal APIs only).
- **Project layout:** single Clean Architecture solution with `Domain`/`Application`/`Infrastructure`/`Api` projects. DTOs go in `ApiModels/`, domain in `Models/` per service.
- **Data:** PostgreSQL, Cosmos DB, Azure Blob Storage. SQL Server + Redis only.
- **Identity:** Azure AD / Entra ID. RS256 JWTs from in-repo Auth service.
- **CI:** GitHub Actions. Azure Pipelines only.
- **Payments:** Stripe / external webhook layer. Payment service is in-repo.

If a task seems to require any of the above, surface it before adding.

## Behavioral guidelines

`.claude/CLAUDE.md` contains general LLM coding guidelines (think before coding, simplicity, surgical changes, goal-driven execution). Read once; they apply to all work in this repo.
