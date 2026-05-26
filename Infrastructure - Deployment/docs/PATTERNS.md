# Implementation Patterns

> Companion to [OVERVIEW.md](OVERVIEW.md), [ARCHITECTURE.md](ARCHITECTURE.md),
> [SYSTEM_DESIGN.md](SYSTEM_DESIGN.md), and [TECH_STACK.md](TECH_STACK.md).
> Those documents describe the deployment topology. This document describes
> the code patterns to follow when changing or extending the platform.

## Current baseline

The repo has moved to **Clean Architecture + Vertical Slices** as the default
service shape. The current decisions are:

- [ADR-0012](../../docs/adr/0012-clean-arch-vsa-default-service-shape.md) -
  default service layout for all microservices and the API Gateway.
- [ADR-0013](../../docs/adr/0013-shared-libs-multi-package-split.md) -
  `ECommerce.Shared` split into capability packages behind an umbrella
  package.
- [adding-a-new-slice.md](../../docs/runbooks/adding-a-new-slice.md) -
  detailed step-by-step slice runbook.

Older docs may mention `Endpoints/`, `ApiModels/`, `Models/`, and
`IntegrationEvents/` as top-level service folders. The current implementation
uses `Features/`, `Domain/`, `Contracts/Integration/`, and `Infrastructure/`
unless an ADR records a service-specific divergence.

## Pattern map

| Pattern | Where it lives | Rule of thumb |
|---|---|---|
| Service boundary | One `*-microservice/` or `api-gateway/` folder with its own `.slnx` | Build and test per service. Treat any root solution as a convenience view, not the service boundary. |
| Vertical slice | `<Service>.Service/Features/<Slice>/` | One inbound trigger per folder: HTTP route, integration event, or saga command. |
| Domain boundary | `<Service>.Service/Domain/` | Aggregates own invariants. Slices orchestrate; they do not hold business rules. |
| Contracts | `<Service>.Service/Contracts/Integration/` | Cross-service payloads live at the boundary. They do not reference service internals. |
| Infrastructure | `<Service>.Service/Infrastructure/` | EF Core, Redis, HTTP clients, carrier/payment adapters, outbox endpoints. |
| Composition root | `<Service>.Service/Program.cs` | A manifest of storage, slices, messaging, observability, health, auth, and mapped endpoints. |
| Shared platform | `shared-libs/ECommerce.Shared.*` | Cross-cutting concerns belong in shared packages, not copy-pasted into services. |
| Architecture guardrails | `<Service>.Tests/Architecture/LayoutTests.cs` and `<Service>.Service.LayoutAnalyzer/` | Boundary rules fail in both `dotnet build` and `dotnet test`. |
| Deployment | Per-service `azure-pipelines.yml`, shared templates, root `kubernetes/` manifests | A service pipeline builds, tests, pushes one image, then applies the service manifest. |

## Default service shape

New services and new service work should start from this shape:

```text
<service>-microservice/
  <Service>.Service.slnx
  azure-pipelines.yml
  <Service>.Service/
    Program.cs
    Features/
      <Slice>/
        <Slice>Endpoint.cs              # HTTP slice only
        <Slice>Handler.cs               # HTTP/read orchestration
        <Slice>CommandHandler.cs        # command consumer, when applicable
        <Slice>EventHandler.cs          # event consumer, when applicable
        <Slice>Request.cs               # HTTP write DTO, when applicable
        <Slice>Response.cs              # HTTP read DTO, when applicable
        <Slice>SliceExtensions.cs       # slice DI contract
        <EventName>IntegrationMap.cs    # publishing slices, when applicable
    Domain/
      Events/
      Abstractions/
    Contracts/
      Integration/
    Infrastructure/
      Data/
      Outbox/
      <adapters>/
    Migrations/
  <Service>.Tests/
    Architecture/LayoutTests.cs
    Features/<Slice>/
  <Service>.Service.LayoutAnalyzer/
```

Intentional divergences are part of the pattern, not accidental drift:

- **Auth** omits `Contracts/` because it does not publish or consume
  integration events.
- **ApiGateway** omits `Domain/` and `Contracts/` because it owns no aggregate
  and publishes no integration events. Its operator API is still vertical-slice
  shaped under `Features/Operator/...`.
- **Basket, Inventory, Shipping, and Saga** do not use the
  `IIntegrationMap`/`DomainEventOutboxInterceptor` seam because they do not have
  a central `DbContext.Translate(...)` style switch to dissolve.
- **Payment** has multi-producer slices where HTTP writes and saga commands can
  raise the same domain event. The mapper is registered globally and is owned by
  one slice, but other slices do not reference that slice.
- **Saga** nests features as `Features/<Saga>/<Trigger>/` because it hosts both
  order and refund saga workflows.

## Composition root pattern

`Program.cs` should read as the service manifest. Keep registrations grouped in
this order unless a service has a clear reason to differ:

1. Datastore and infrastructure adapters.
2. Outbox, when the service publishes through SQL-backed state changes.
3. Slice registrations via `Add<Slice>Slice()`.
4. Platform messaging through `AddPlatformEventBus(...)`, plus publisher and/or
   subscriber extensions as needed.
5. Observability, health checks, JWT auth, authorization policies, and OpenAPI.
6. Runtime middleware and endpoint mapping.

Representative examples:

- `Order` wires SQL, outbox, Redis price provider, HTTP slices, event slices,
  publisher/subscriber messaging, JWT auth, health probes, and internal outbox
  endpoints.
- `Basket` wires Redis, HTTP slices, subscriber-only messaging, and no JWT
  middleware because its public access is enforced at the gateway.
- `ApiGateway` wires the proxy provider switch, dead-letter persistence, the
  outbox failure poller, operator slices, and `RequireOperator` authorization.

`Program.cs` should not contain feature logic. If the file starts accumulating
business rules, move that work into a slice handler or infrastructure adapter.

## Vertical slice pattern

One inbound trigger equals one slice. Do not bundle multiple routes or messages
into one folder because they touch the same aggregate.

HTTP slices use:

- `Map<Slice>()` on an endpoint class.
- `HandleAsync(...)` on an `internal sealed` handler.
- Request/response DTOs inside the slice folder.
- `Add<Slice>Slice()` to register the handler and any slice-owned services.

Message slices use:

- `IEventHandler<TEvent>` for integration events or saga commands.
- `AddEventHandler<TEvent, THandler>()` inside the slice extension.
- Idempotent handling based on `Event.Id`, `SagaId`, or the relevant business key.

Cross-slice references are prohibited. If two slices need similar code, duplicate
first. Extract only on the third use, and extract to:

- `Domain/` for business behavior or invariants.
- `Features/Shared/` only for pure feature helpers.
- `Infrastructure/` for adapters or persistence concerns.

## Domain and data access

Write slices load an aggregate through the service store abstraction, call domain
methods, then persist. The handler is orchestration; the aggregate owns validity.

Read slices may project directly from EF Core contexts into response DTOs. They
do not need to hydrate aggregates just to return a query model.

Infrastructure owns persistence details:

- `Infrastructure/Data/EntityFramework/` registers EF Core, retry policy, and
  the service store implementation.
- `MigrateDatabase()` is a service-local extension used by QA/dev seeding paths.
- Design-time factories stay with EF infrastructure so migrations can be added
  without running `Program.cs`.

Do not let `Contracts/Integration/` depend on `Domain/`, `Features/`, or
`Infrastructure/`. Contract payloads must remain portable message schemas.

## Messaging and outbox

The platform publishes through the shared provider-aware messaging surface:

```csharp
builder.Services.AddPlatformEventBus(builder.Configuration)
    .AddPlatformEventPublisher(builder.Configuration)
    .AddPlatformSubscriberService(builder.Configuration);
```

Use only the extensions the service needs:

- Publisher-only services omit `AddPlatformSubscriberService(...)`.
- Subscriber-only services omit `AddPlatformEventPublisher(...)`.
- Services that both publish and consume use all three.

`Messaging:Provider` selects RabbitMQ by default or Azure Service Bus when set
to `AzureServiceBus`. Feature code should not branch on the provider and should
not reference broker-specific types.

SQL-backed publishing services use the transactional outbox:

- Register `AddOutbox(builder.Configuration)`.
- Apply `app.ApplyOutboxMigrations()` only in the existing QA/dev migration
  path.
- Expose `/internal/outbox/failed` through `RegisterInternalOutboxEndpoints()`.
  The endpoint must require `RequireService`.
- Prefer `IOutboxUnitOfWork` for new business code. It owns the execution
  strategy, transaction, returned events, and outbox telemetry.
- Use `IOutboxStore` directly only in infrastructure, polling, tests, and
  internal outbox endpoints.

When changing event payloads, retry policy, queue names, or provider selection,
audit both the consumer handlers and the API Gateway dead-letter/outbox poller.

## Integration contract conventions

Service-owned integration events live in `Contracts/Integration/`. Saga command
payloads shared across services live in `ECommerce.Shared.Contracts`.

Every message should preserve the correlation chain:

- `CorrelationId` ties work across the end-to-end business transaction.
- `CausationId` points to the message that triggered the new message.
- `SagaId` is required for saga participant commands and replies.

The current order fulfillment pattern is orchestrator-led:

1. `Order` publishes `OrderCreatedEvent`.
2. `Saga` creates saga state and publishes commands.
3. `Inventory`, `Payment`, `Order`, and `Shipping` consume commands and publish
   reply events.
4. `Saga` advances from reply events and issues the next command or
   compensation.

Participants should not coordinate the order saga directly with each other.
New fulfillment behavior belongs in Saga unless an ADR deliberately changes the
coordination model.

## Gateway and operator patterns

The API Gateway owns edge concerns:

- Runtime proxy switch through `Gateway:Provider`, defaulting to YARP with
  Ocelot as the fallback.
- JWT validation and role-based access before traffic reaches services.
- Combined OpenAPI aggregation.
- Operator endpoints under `/operator/api/failures`.
- Dead-letter persistence, replay, discard, and batch replay.
- Optional outbox failure polling from service-owned `/internal/outbox/failed`
  endpoints.

Operator features follow the same slice pattern as services. The operator route
group must require `RequireOperator`; service-to-service polling must use
`RequireService` on the service side.

## Authentication and authorization

Auth owns token issuance:

- `POST /login` issues user JWTs.
- `POST /token` issues service tokens for `client_credentials`.
- JWKS discovery lets resource services validate RS256 tokens without sharing
  signing secrets.

Resource services use:

- `AddJwtAuthentication(builder.Configuration)` to register JWT validation.
- `UseJwtAuthentication()` after endpoint/health/OpenAPI setup and before
  protected traffic requires it.
- `AddRequireServicePolicy()` for `/internal/*` endpoints.
- `AddRequireOperatorPolicy()` for gateway operator endpoints.

Do not introduce shared symmetric secrets between services. If an endpoint is
internal, gate it with `RequireService`; user tokens must not reach it.

## Observability, health, and OpenAPI

Every service should expose the platform observability surface:

- `AddPlatformObservability("<ServiceName>", ...)` for traces, metrics, logs,
  and any service-specific meters or activity sources.
- `UsePrometheusExporter()` for `/metrics`.
- `AddPlatformHealthChecks()` plus dependency probes for SQL, Redis, or the
  broker.
- `MapPlatformHealthChecks()` for `/health/live` and `/health/ready`.
- `AddPlatformOpenApi("<service-id>")` and `UsePlatformOpenApi()` where the
  service contributes to the gateway's combined Swagger UI.

Custom metrics should be named for the business signal, such as
`products-created`, `reservation-latency-ms`, or `payments_total`. Prefer adding
the metric near the slice or infrastructure component that owns the event.

## Shared library package pattern

Shared platform code is distributed through a local NuGet feed. Consumers still
reference the umbrella `ECommerce.Shared` package, while the source is split
into capability packages:

- `ECommerce.Shared.Kernel`
- `ECommerce.Shared.EventBus`
- `ECommerce.Shared.RabbitMq`
- `ECommerce.Shared.AzureServiceBus`
- `ECommerce.Shared.DeadLetter`
- `ECommerce.Shared.Platform`
- `ECommerce.Shared.Contracts`
- `ECommerce.Shared.Testing.Qa`
- `ECommerce.Shared` umbrella package

Inside a capability package:

- `Abstractions/` contains public contracts, options, and POCOs.
- `Impl/` contains concrete implementations.
- `Composition/` contains DI extension methods.

All shared packages version together through `shared-libs/Directory.Build.props`.
After shared-library edits, pack the shared solution, publish the generated
packages into `local-nuget-packages/`, then bump consumers deliberately. Do not
expect consumers to see shared-library source changes through project references.

## Testing and feedback loops

Tests mirror the source shape:

- Slice tests live under `<Service>.Tests/Features/<Slice>/`.
- Aggregate invariants live under `<Service>.Tests/Domain/`.
- Architecture rules live under `<Service>.Tests/Architecture/LayoutTests.cs`.
- Integration hosts use `WebApplicationFactory<Program>`, which depends on each
  service ending `Program.cs` with `public partial class Program { }`.

The standard local loop for touched services is:

```bash
cd <service>-microservice
dotnet build
dotnet test
dotnet format --verify-no-changes --verbosity minimal
```

The root Husky.Net pre-commit hook runs format, build, and a fast Basket test
slice. Cross-service changes still require manually running the affected service
test suites before pushing.

## Deployment and CI/CD patterns

Each deployable service owns an `azure-pipelines.yml` and extends the shared
pipeline templates under `Infrastructure - Deployment/pipelines/templates/`.

The normal flow is:

1. Path-filtered pipeline trigger for the touched service, `shared-libs/`, or
   shared pipeline templates.
2. Restore, format, build, test, publish, Docker build, and Docker push.
3. Deploy stage creates or updates Kubernetes secrets.
4. Deploy stage applies the service manifest from the root `kubernetes/` folder
   with the exact image tag produced by the build.

Dev can deploy from Microsoft-hosted agents. Staging and Production use the
self-hosted pool so private AKS and private data endpoints remain reachable.
GitHub Actions is not the deployment path for this repo.

## Change checklist

Before changing or adding a pattern-sensitive area, check:

- Is this a new inbound trigger? Add a vertical slice.
- Is this cross-cutting? Put it in `ECommerce.Shared`, not a single service.
- Does this publish after SQL state changes? Use the outbox and preserve
  correlation metadata.
- Does this affect saga flow? Update Saga first, then participant commands and
  reply events.
- Does this affect message routing, retry, or payloads? Audit consumers and the
  DLQ/operator path.
- Does this add a service boundary exception? Record it in an ADR before it
  becomes undocumented drift.
- Did architecture tests and layout analyzers remain green?
