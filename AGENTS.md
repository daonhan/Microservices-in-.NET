# AGENT.md

Guidance for AI Agent in this repo. See [README.md](README.md), [CONTEXT.md](CONTEXT.md), [.claude/CLAUDE.md](.claude/CLAUDE.md) (behavioral rules).

## Repo shape

.NET microservices monorepo, **net10.0**, no root solution. Each `*-microservice/`, `api-gateway/`, `shared-libs/` is its own `.slnx`.

Services (port, datastore): basket 8000 Redis · order 8001 SQL+Redis · product 8002 SQL · auth 8003 SQL · api-gateway 8004 — · inventory 8005 SQL · shipping 8006 SQL · payment 8007 SQL.

## Build / test / run

Per-solution from the service directory.

```bash
cd <svc>-microservice && dotnet build
cd <svc>-microservice && dotnet test
dotnet test --filter "FullyQualifiedName~OrderEndpointTests"   # class
dotnet test --filter "DisplayName~Given_X_When_Y_Then_Z"       # single
dotnet format --verify-no-changes --verbosity minimal          # mirrors pre-commit
docker compose up --build                                       # full stack
docker compose up sql rabbitmq redis -d                         # infra only
```

`Directory.Build.props` sets `TreatWarningsAsErrors` + `EnforceCodeStyleInBuild`; its `NoWarn` list documents intentional exemptions.

## Pre-commit (Husky.Net)

Activate once: `dotnet tool restore && dotnet husky install`. Hook runs `dotnet format --verify-no-changes`, `dotnet build --no-restore`, then Basket tests only — **run other suites manually before pushing cross-service changes**.

Known WSL/virtiofs issue: pre-commit may fail at `dotnet build --no-restore` with `MSB3248 No such device` due to root-owned `bin/obj`. Not a regression. Workaround from a writable host shell:

```bash
find . -type d \( -name bin -o -name obj \) -prune -exec rm -rf {} +
dotnet restore && dotnet husky run --group pre-commit
```

If cleanup is blocked, commit from a host where hooks pass. **Do not bypass hooks.**

## Shared library (`ECommerce.Shared`)

Consumed as a NuGet package (not project ref). Local feed: `local-nuget-packages/` (gitignored). After edits:

```bash
cd shared-libs/ECommerce.Shared
dotnet pack -c Release
dotnet nuget push bin/Release/*.nupkg -s ../../local-nuget-packages
# bump <Version> in .csproj so consumers pick it up
```

Consumers see no change until version bump + new `.nupkg` in feed.

## Cross-service architecture

Read together: each service's `Program.cs` (composition root, uses `ECommerce.Shared` extensions: `AddSqlServerDatastore`, `AddOutbox`, `AddRabbitMqEventBus`, `AddRabbitMqEventPublisher`, `AddRabbitMqSubscriberService`, `AddEventHandler<TEvent,THandler>`, `AddPlatformObservability`, `AddPlatformHealthChecks`, `AddPlatformOpenApi`); `shared-libs/ECommerce.Shared/Infrastructure/` (`EventBus/`, `RabbitMq/` — fanout `ecommerce-exchange`, OTEL via headers; `Outbox/` — `OutboxBackgroundService`, services that publish need `AddOutbox(...)` + `app.ApplyOutboxMigrations()` in Dev). New cross-cutting concerns belong in `ECommerce.Shared`.

**Saga (no orchestrator):** `OrderCreatedEvent` → Inventory reserves → `StockReserved`/`StockReservationFailed` → Order emits `OrderConfirmed`/`OrderCancelled` → Inventory commits/releases → Payment authorizes/captures (`PaymentAuthorized|Captured|Failed|Refunded`) → Shipping on `StockCommitted` (`ShipmentCreated|Dispatched|Delivered|Cancelled|Returned|Failed`). Touching one leg without the others desyncs the flow. Events: `IntegrationEvents/Events/`; handlers: `IntegrationEvents/EventHandlers/`.

Per-service layout: `Endpoints/` (Minimal API), `ApiModels/` (DTOs), `Models/` (domain), `Infrastructure/Data/`, `IntegrationEvents/`, `Migrations/`. Keep DTOs vs domain split.

## API Gateway provider switch

Gateway compiles both YARP and Ocelot. `Gateway:Provider` (env `Gateway__Provider`) = `Yarp` (default) or `Ocelot`; unknown values fail fast. Routes/port/auth/health/metrics identical across both.

## DLQ + operator API

Retry-exhausted messages dead-letter to `ecommerce-dlq` exchange. API Gateway poller persists them (plus failed outbox rows from each service's `/internal/outbox/failed`, gated by `RequireService`) into gateway-owned `dead_letter_messages`. Operator endpoints under `/operator/api/failures*` (Bearer + `Operator` claim): list/detail/single replay/batch replay/discard. Replay re-publishes to `OriginalQueue`; `dlq.replay` spans carry original `CorrelationId`. Counters: `dlq_messages_total`, `dlq_replays_total`, `dlq_discards_total`.

When changing event payloads, retry policy, or queue routing, audit both consumer side **and** the DLQ poller.

## Authentication

RS256 user JWTs from Auth (`POST /login`); `client_credentials` service tokens (`POST /token`). Resources validate via `AddJwtAuthentication()` (fetches+caches `/jwks` — no shared secret). `/internal/*` gated by `RequireService` policy (`scope=service`); user tokens cannot reach. Dev keys: `auth-microservice/Auth.Service/dev-keys/`.

## CI/CD

Azure Pipelines per service (`azure-pipelines.yml`). Bicep provisions AKS/ACR/Azure SQL/Service Bus/App Insights. Deploys to `ecommerce-{dev,staging,prod}` namespaces. **GitHub Actions is not used.**

## Conventions

- File-scoped namespaces, `var` preferred, usings outside namespace (`.editorconfig`).
- Test names `Given_When_Then` with underscores (`CA1707` suppressed).
- `**/Migrations/*.cs` marked `generated_code = true` — don't hand-edit style.
- Per-service `IDesignTimeDbContextFactory` so `dotnet ef migrations add` works without running `Program.cs`.
- Integration tests use `WebApplicationFactory<Program>`; each service has `public partial class Program { }` at end of `Program.cs`.

## Behavioral

Apply `.claude/CLAUDE.md` (think first, simplicity, surgical changes, goal-driven). Make only changes the user asked for; match existing style; prefer smallest correct change; push back on over-engineering; state a brief plan + success criteria for non-trivial work.
