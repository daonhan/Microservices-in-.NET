# Shared Library — `ECommerce.Shared.*`

Shared platform code ships as nine direct capability packages plus the `ECommerce.Shared` umbrella compatibility metapackage. All packages are published to the local NuGet feed under [`local-nuget-packages/`](https://github.com/daonhan/Microservices-in-.NET/tree/main/local-nuget-packages) at one lockstep version, and production services reference only the direct packages they need.

## What's inside

```mermaid
graph LR
    subgraph ECommerce.Shared
        Kernel[Kernel]
        Contracts[Contracts]
        Jwt[JWT auth]
        Bus[Platform messaging]
        Sub[Provider subscriber]
        Outbox[Transactional Outbox]
        DLQ[Dead-letter operations]
        Obs[Observability]
        Health[Health Checks]
        Qa[QA helpers]
    end
    Svc[Any service] --> Kernel
    Svc --> Contracts
    Svc --> Jwt
    Svc --> Bus
    Svc --> Sub
    Svc --> Outbox
    Svc --> DLQ
    Svc --> Obs
    Svc --> Health
    Svc --> Qa
```

| Package | Purpose | Key extension methods / types |
|---|---|---|
| `ECommerce.Shared.Kernel` | Common event, options, and telemetry primitives | `Event`, `MessagingOptions`, telemetry convention constants, `MetricFactory` |
| `ECommerce.Shared.EventBus` | Event bus abstractions, handler registration, and transactional outbox | `IEventBus`, `IEventHandler<TEvent>`, `AddEventHandler<TEvent,THandler>()`, `AddOutbox<TContext>()`, `IOutboxUnitOfWork`, `IOutboxStore` |
| `ECommerce.Shared.RabbitMq` | RabbitMQ provider adapter | `RabbitMqEventBus`, `RabbitMqSubscriberService`, `IRabbitMqConnection`, RabbitMQ health probe |
| `ECommerce.Shared.AzureServiceBus` | Azure Service Bus provider adapter | Azure Service Bus event bus, subscriber service, dead-letter receiver/publisher adapters |
| `ECommerce.Shared.Messaging` | Provider-aware messaging composition | `Messaging:Provider`, `AddPlatformEventBus()`, `AddPlatformEventPublisher()`, `AddPlatformSubscriberService()` |
| `ECommerce.Shared.DeadLetter` | Provider-agnostic DLQ capture, persistence, replay, and discard | `IDeadLetterCapture`, `IDeadLetterPublisher`, `IDeadLetterReplayer`, `IDeadLetterDiscarder`, DLQ telemetry |
| `ECommerce.Shared.Platform` | Auth, authorization, observability, health, OpenAPI, SQL datastore helpers | `AddJwtAuthentication()`, `AddRequireOperatorPolicy()`, `AddRequireServicePolicy()`, `AddPlatformObservability()`, `AddPlatformHealthChecks()`, `AddPlatformOpenApi()`, `AddSqlServerDatastore()` |
| `ECommerce.Shared.Contracts` | Shared cross-service contracts | Saga command POCOs such as `ReserveStockCommand`, `AuthorizePaymentCommand`, `CreateShipmentCommand` |
| `ECommerce.Shared.Testing.Qa` | QA/test seeding helpers | `QaPersonas`, `QaSeedingExtensions` |

This split is recorded in [ADR-0013](https://github.com/daonhan/Microservices-in-.NET/blob/main/docs/adr/0013-shared-libs-multi-package-split.md). Production services narrow-pin direct capability packages and avoid the umbrella unless broad compatibility is intentional; the package selection and version sweep workflow lives in [docs/runbooks/shared-libs-versioning.md](https://github.com/daonhan/Microservices-in-.NET/blob/main/docs/runbooks/shared-libs-versioning.md).

## Public DI surface

| Extension | Purpose |
|---|---|
| `AddJwtAuthentication(IConfiguration)` | Configures JWT Bearer validation for RS256 user and service tokens via JWKS and the standard claim map |
| `UseJwtAuthentication()` | Middleware pair (`UseAuthentication()` + `UseAuthorization()`) |
| `AddRequireOperatorPolicy()` / `AddRequireServicePolicy()` | Register the `RequireOperator` and `RequireService` authorization policies (see [Authorization policies](#authorization-policies)) |
| `AddPlatformEventBus(IConfiguration)` | Registers the configured messaging provider; `Messaging:Provider` defaults to `RabbitMq` and can be set to `AzureServiceBus` |
| `AddPlatformEventPublisher(IConfiguration)` | Registers `IEventBus` for publishing through the selected provider |
| `AddPlatformSubscriberService(IConfiguration)` | Hosts the selected provider's subscriber service |
| `AddEventHandler<TEvent, THandler>()` | Keyed DI so one service can register many handlers |
| `AddOutbox<TContext>()` | Outbox table, `OutboxBackgroundService`, and write helpers |
| `AddPlatformObservability()` | OTLP traces + Prometheus metrics + OTLP logs |
| `AddPlatformHealthChecks()` + `MapPlatformHealthChecks()` | `/health/live`, `/health/ready` |
| `AddSqlServerProbe()`, `AddRedisProbe()`, `AddRabbitMqProbe()` | Per-dependency readiness probes |
| `AddPlatformOpenApi()` + `UsePlatformOpenApi()` | Swashbuckle with the platform-wide JWT Bearer security scheme and XML-comment pickup; UI is gateway-only, so services expose `GET /swagger/v1/swagger.json` (dev/staging) and the gateway aggregates them — see [Service-API-Gateway § Combined Swagger UI](Service-API-Gateway#combined-swagger-ui) |

## Key abstractions

| Type | Role |
|---|---|
| `Event` | Base class for all integration events (carries `Id`, `OccurredAt`) |
| `IEventBus` | Publish an `Event` to the fanout exchange |
| `IEventHandler<TEvent>` | Subscriber contract; implementations are keyed-DI-registered |
| `IRabbitMqConnection` | RabbitMQ adapter connection used when `Messaging:Provider=RabbitMq` |
| `IDeadLetterCapture` | Provider-selected broker dead-letter capture hosted by the gateway |
| `IDeadLetterPublisher` | Provider-selected replay publisher used by the gateway operator API |
| `IOutboxUnitOfWork` | **Preferred caller-facing seam** for transactional publishing — runs business work under the execution strategy + ambient transaction and enqueues the returned events in the same scope |
| `IOutboxStore` | Low-level "persist + enqueue event" primitive; backs `IOutboxUnitOfWork` and stays available for infrastructure, outbox polling, and `/internal/outbox` routes |
| `MetricFactory` | Cached creation of Counters and Histograms |

## Authorization policies

`ECommerce.Shared.Authentication.AuthorizationPolicies` exposes named policies keyed off the `user_role` claim issued by [Service-Auth](Service-Auth):

| Policy | Required `user_role` | Use case |
|---|---|---|
| `RequireOperator` | `Operator` | Operator-only UI/endpoints (e.g. DLQ replay UI in the gateway) |
| `RequireService` | `service` | Service-to-service `/internal/*` endpoints called by other backends using a `client_credentials` token from `POST /token` |

Apply with the standard `RequireAuthorization("RequireService")` on a Minimal API endpoint. The policy short-circuits with `403` if the JWT lacks the expected role.

## Transactional Outbox — why it matters

Without the outbox, a service that writes its domain row and publishes its event separately risks two failure modes:

1. DB commits, broker publish fails → downstream never sees the change.
2. Broker publish succeeds, DB rolls back → phantom event.

With the outbox, the business row and the outbox row are written in the **same** transaction. A background service periodically polls the outbox and publishes. At-least-once delivery + idempotent handlers = exactly-once effect.

### Preferred caller seam: `IOutboxUnitOfWork`

Business call sites should publish through `IOutboxUnitOfWork`, not by hand-rolling the
`CreateExecutionStrategy + TransactionScope + AddOutboxEvent + Complete` ceremony:

```csharp
await outboxUnitOfWork.ExecuteAsync(outboxStore.CreateExecutionStrategy(), async () =>
{
    await store.SaveChangesAsync();           // business state change
    return new Event[] { new SomethingHappenedEvent(...) };  // enqueued in the same transaction
});
```

The seam owns the execution strategy retry loop, the ambient transaction, enqueuing the
returned events, and outbox telemetry. Returning an empty list commits the business work
with no events.

This seam is **provider-neutral**: callers never reference RabbitMQ- or Azure Service
Bus-specific types. Delivery of the enqueued rows is still selected by `Messaging:Provider`
(see [Canonical messaging wiring](#canonical-messaging-wiring)), so switching brokers needs
no call-site changes.

`IOutboxStore` is **not removed** — it remains the low-level primitive that backs
`IOutboxUnitOfWork` and is still used directly by infrastructure, the outbox poller, and
`/internal/outbox` routes. New business code should prefer the unit-of-work seam.

## Observability wiring

`AddPlatformObservability()` wires:

- **Traces**: ASP.NET Core, HttpClient, EF Core, RabbitMQ span context propagation → OTLP → Jaeger
- **Metrics**: Runtime, ASP.NET Core, custom via `MetricFactory` → scraped on `/metrics` by Prometheus
- **Logs**: Structured logs → OTLP → Loki

## Canonical messaging wiring

New service composition roots should use the provider-aware platform extensions, then register the service's event handlers:

```csharp
builder.Services.AddPlatformEventBus(builder.Configuration)
    .AddPlatformEventPublisher(builder.Configuration)
    .AddPlatformSubscriberService(builder.Configuration)
    .AddEventHandler<OrderCreatedEvent, OrderCreatedEventHandler>();
```

Omit the publisher extension for subscriber-only services and omit the subscriber extension for publisher-only services. RabbitMQ-specific readiness probes can stay on services that need RabbitMQ local health checks.

Gateway DLQ capture and replay are provider-selected through the same `Messaging:Provider` value. RabbitMQ captures from the shared DLQ queue; Azure Service Bus captures from configured subscription dead-letter subqueues. The operator routes and `dead_letter_messages` schema stay unchanged. Details: [Provider-Agnostic DLQ Capture and Replay](https://github.com/daonhan/Microservices-in-.NET/blob/main/docs/runbooks/provider-agnostic-dlq.md).

See [Observability](Observability) for the full pipeline.

## Building the library

```bash
dotnet pack -c Release shared-libs/ECommerce.Shared.slnx
cp shared-libs/**/bin/Release/*.nupkg local-nuget-packages/
```

Services consume direct packages via `nuget.config` in each microservice folder, pointing at `../local-nuget-packages`.
