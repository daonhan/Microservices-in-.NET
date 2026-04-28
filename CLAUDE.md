# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Repo shape

.NET microservices monorepo. Each top-level `*-microservice/` (and `api-gateway/`, `shared-libs/`) is an independent solution using a `.slnx` file (no root `.sln`). All projects target **net10.0** despite README mentioning .NET 8 — trust the `.csproj` files.

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

`.husky/task-runner.json` runs on commit:
1. `dotnet format --verify-no-changes`
2. `dotnet build --no-restore`
3. `dotnet test basket-microservice/Basket.Service.slnx --no-build --no-restore`

Only Basket tests run pre-commit. Run other service test suites manually before pushing changes that cross service boundaries.

## Shared library workflow (`ECommerce.Shared`)

`shared-libs/ECommerce.Shared` is consumed as a **NuGet package** (e.g. `<PackageReference Include="ECommerce.Shared" Version="2.0.0" />`), not a project reference. The package is published to `local-nuget-packages/` (gitignored). After editing the shared lib:

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

3. **Saga between Order and Inventory** — `OrderCreatedEvent` → Inventory reserves stock → `StockReserved`/`StockReservationFailed` → Order emits `OrderConfirmed`/`OrderCancelled` → Inventory commits or releases. Touching either side without considering both will desynchronize the flow. Event types live in each service's `IntegrationEvents/Events/`; handlers in `IntegrationEvents/EventHandlers/`.

Each service follows the same internal layout: `Endpoints/` (Minimal API handlers), `ApiModels/` (DTOs), `Models/` (domain), `Infrastructure/Data/` (EF Core or Redis), `IntegrationEvents/`, `Migrations/`. Keep this split — DTOs in `ApiModels`, domain types in `Models`.

## API Gateway provider switch

The gateway compiles **both** YARP and Ocelot. `Gateway:Provider` (env `Gateway__Provider`) selects at startup; values `Yarp` (default) or `Ocelot`. Unknown values fail fast. Logged at boot as `ApiGateway starting with provider=...`. Routes, port, auth, health checks, and metrics are identical across both — no client-side change needed when switching.

## Conventions worth knowing

- File-scoped namespaces, `var` preferred, usings outside namespace (enforced by `.editorconfig`, warning level).
- Test names use `Given_When_Then` with underscores (suppressed `CA1707`).
- EF Core migrations under `**/Migrations/*.cs` are marked `generated_code = true` — don't hand-edit style.
- `IDesignTimeDbContextFactory` is implemented per service so `dotnet ef migrations add ...` works without running `Program.cs`.
- Integration tests use `WebApplicationFactory<Program>`; each service exposes `public partial class Program { }` at the bottom of `Program.cs` to make this work.

## Behavioral guidelines

`.claude/CLAUDE.md` contains general LLM coding guidelines (think before coding, simplicity, surgical changes, goal-driven execution). Read once; they apply to all work in this repo.
