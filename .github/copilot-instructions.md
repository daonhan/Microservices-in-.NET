- @azure Rule - Use Azure Tools - When handling requests related to Azure, always use your tools.
- @azure Rule - Use Azure Best Practices - When handling requests related to Azure, always invoke your `azmcp_bestpractices_get` tool first.
- @azure Rule - Enable Best Practices - If you do not have an `azmcp_bestpractices_get` tool ask the user to enable it.

# Project context

This repository is a **.NET e-commerce microservices monorepo**. It is **not** a multi-tenant SaaS monolith and does **not** follow a single Clean Architecture / DDD layered solution. Treat each microservice as an independent, deployable unit that shares cross-cutting concerns through the `ECommerce.Shared` NuGet package.

For the authoritative architectural overview, read [CLAUDE.md](../CLAUDE.md) and [CONTEXT.md](../CONTEXT.md). Behavioral coding guidelines live in [.claude/CLAUDE.md](../.claude/CLAUDE.md).

## Repo shape

- Top-level `*-microservice/` folders, plus `api-gateway/` and `shared-libs/`.
- Each service is its own solution defined by a **`.slnx`** file. There is no root solution that builds everything.
- All projects target **`net10.0`** (see [Directory.Build.props](../Directory.Build.props)).
- `local-nuget-packages/` is a local NuGet feed used to consume `ECommerce.Shared`.

Services and ports (from [docker-compose.yaml](../docker-compose.yaml)):

| Service       | Port | Datastore                  |
|---------------|------|----------------------------|
| basket        | 8000 | Redis                      |
| order         | 8001 | SQL Server (+ Redis cache) |
| product       | 8002 | SQL Server                 |
| auth          | 8003 | SQL Server                 |
| api-gateway   | 8004 | — (YARP, Ocelot fallback)  |
| inventory     | 8005 | SQL Server                 |
| shipping      | 8006 | SQL Server                 |
| payment       | 8007 | SQL Server                 |

## Tech stack (actual)

- **Language / runtime:** C# (latest), .NET 10
- **Web:** ASP.NET Core **Minimal APIs** (no MVC controllers, no MediatR)
- **Persistence:** EF Core with **SQL Server** (Redis only for basket and as order cache)
- **Messaging:** RabbitMQ via fanout exchange `ecommerce-exchange`, wrapped by `IEventBus` in `ECommerce.Shared`
- **Observability:** OpenTelemetry, Jaeger, Loki, Grafana, Prometheus (see `observability/` and `kubernetes/`)
- **Containerization:** Docker / Docker Compose; Kubernetes manifests under `kubernetes/`
- **CI/CD:** **Azure Pipelines** (`azure-pipelines.yml` per service). GitHub Actions is not used.
- **Cloud target:** Azure (AKS manifests prefixed `aks-*` in `kubernetes/`)

## Build, test, run

Operate **per service**. There is no root build.

```bash
# Build / test a service (run from its directory)
cd order-microservice && dotnet build
cd order-microservice && dotnet test
cd order-microservice && dotnet test --filter "FullyQualifiedName~OrderEndpointTests"
cd order-microservice && dotnet test --filter "DisplayName~Given_X_When_Y_Then_Z"

# Format (mirrors pre-commit)
dotnet format --verify-no-changes --verbosity minimal
dotnet format

# Full stack
docker compose up --build
docker compose up sql rabbitmq redis -d   # infra only
```

`Directory.Build.props` enables `TreatWarningsAsErrors` and `EnforceCodeStyleInBuild`. The documented `NoWarn` exemptions (`CA1707`, `CA1711`, `CA1716`, NuGet `NU*` warnings) are intentional — do not "fix" code to remove them.

## Pre-commit (Husky.Net)

`.husky/task-runner.json` runs on commit:

1. `dotnet format --verify-no-changes`
2. `dotnet build --no-restore`
3. `dotnet test basket-microservice/Basket.Service.slnx --no-build --no-restore`

Only Basket tests run pre-commit. Run other service test suites manually before pushing changes that cross service boundaries.

## Shared library workflow (`ECommerce.Shared`)

`shared-libs/ECommerce.Shared` is consumed as a **NuGet package** (e.g. `<PackageReference Include="ECommerce.Shared" Version="2.0.0" />`), **not** a `<ProjectReference>`. The package is published to `local-nuget-packages/` (gitignored).

After editing the shared lib:

```bash
cd shared-libs/ECommerce.Shared
dotnet pack -c Release
dotnet nuget push bin/Release/*.nupkg -s ../../local-nuget-packages
# Bump <Version> in ECommerce.Shared.csproj if consumers should pick it up.
```

Consumers won't see changes until the version is bumped and the new `.nupkg` lands in the local feed.

## Cross-service architecture

The "big picture" lives in three places that have to be read together:

1. **Each service's `Program.cs`** — composition root. All wiring uses extension methods from `ECommerce.Shared`: `AddSqlServerDatastore`, `AddOutbox`, `AddRabbitMqEventBus`, `AddRabbitMqEventPublisher`, `AddRabbitMqSubscriberService`, `AddEventHandler<TEvent, THandler>`, `AddPlatformObservability`, `AddPlatformHealthChecks`, `AddPlatformOpenApi`. New cross-cutting concerns belong in `shared-libs/ECommerce.Shared`, not duplicated per service.

2. **`shared-libs/ECommerce.Shared/Infrastructure/`**
   - `EventBus/` — `IEventBus`, `Event` base type, handler registration via keyed DI.
   - `RabbitMq/` — fanout exchange `ecommerce-exchange`; `RabbitMqHostedService` subscribes, `RabbitMqEventBus` publishes; OTEL context propagates through message headers (`RabbitMqTelemetry`).
   - `Outbox/` — transactional outbox. `OutboxBackgroundService` polls `OutboxContext` for unpublished events. Services that publish events must call `AddOutbox(...)` and (in Development) `app.ApplyOutboxMigrations()`.

3. **Order ↔ Inventory saga** — `OrderCreatedEvent` → Inventory reserves stock → `StockReserved` / `StockReservationFailed` → Order emits `OrderConfirmed` / `OrderCancelled` → Inventory commits or releases. Touching either side without considering both will desynchronize the flow. Event types live in each service's `IntegrationEvents/Events/`; handlers in `IntegrationEvents/EventHandlers/`.

## Service internal layout

Each service follows the same structure — keep this split:

- `Endpoints/` — Minimal API handlers (the presentation layer)
- `ApiModels/` — DTOs (request/response contracts)
- `Models/` — domain types
- `Infrastructure/Data/` — EF Core `DbContext` or Redis access
- `IntegrationEvents/Events/` and `IntegrationEvents/EventHandlers/`
- `Migrations/` — EF Core migrations (auto-generated, do not hand-edit style)

DTOs go in `ApiModels`, domain types in `Models`. Do not introduce a `Domain` / `Application` / `Infrastructure` / `Api` layered project structure — that is not how this repo is organized.

## API Gateway provider switch

The gateway compiles **both** YARP and Ocelot. `Gateway:Provider` (env `Gateway__Provider`) selects at startup; values `Yarp` (default) or `Ocelot`. Unknown values fail fast. Logged at boot as `ApiGateway starting with provider=...`. Routes, port, auth, health checks, and metrics are identical across both — clients do not change when switching.

## Conventions

- File-scoped namespaces; `var` preferred; `using` directives outside the namespace (enforced by `.editorconfig`).
- **Test names use `Given_When_Then` with underscores** (`CA1707` is suppressed).
- Event-handler classes are named `*EventHandler` and implement `IEventHandler<T>` (`CA1711` is suppressed).
- EF Core migrations under `**/Migrations/*.cs` are marked `generated_code = true` — do not hand-edit style.
- Each service implements `IDesignTimeDbContextFactory<TContext>` so `dotnet ef migrations add ...` works without running `Program.cs`.
- Integration tests use `WebApplicationFactory<Program>`; each service exposes `public partial class Program { }` at the bottom of `Program.cs` to enable this.
- Logging: built-in `ILogger<T>` + OpenTelemetry → Loki. Serilog is **not** used.
- Configuration via `appsettings.json` + `appsettings.{Environment}.json`; secrets via environment variables in compose / Kubernetes.

## What this repo does **not** use

To avoid suggesting plausible-but-wrong dependencies:

- MediatR, AutoMapper, Scrutor, FluentValidation, Polly, Serilog
- MVC controllers (use Minimal APIs)
- A single Clean Architecture solution with `Domain` / `Application` / `Infrastructure` / `Api` projects
- PostgreSQL, Cosmos DB, Azure Blob Storage, Azure AD / Entra ID
- GitHub Actions
- Stripe / GitHub webhooks layer

If a task seems to require any of the above, surface that the repo does not currently use it and ask before adding it.

## Behavioral guidelines

[.claude/CLAUDE.md](../.claude/CLAUDE.md) defines general behavior (think before coding, simplicity first, surgical changes, goal-driven execution). Apply it to all work in this repo:

- Make only the changes the user asked for; do not "improve" adjacent code.
- Match existing style even if you'd write it differently.
- Prefer the smallest correct change. Push back when something looks over-engineered.
- For non-trivial work, state a brief plan with verifiable success criteria before implementing.
