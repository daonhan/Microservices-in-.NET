# Plan: ApiGateway Clean Architecture + Vertical Slice Pilot (ninth and final)

> Source PRD: `docs/prd/PRD-ApiGateway-CleanArch-VSA-Pilot.md`
> Branch: `refactor/api-gateway-vsa`

## Context

`ApiGateway` is the last service in the monorepo still on the legacy "module" layout: two top-level folders (`Gateway/`, `Operator/`) each holding a static `*Module` god-class that mixes endpoint mapping, DI wiring, configuration binding, and helper records. The other eight services (Order/Product/Basket/Auth/Inventory/Shipping/Payment/Saga) have already migrated to Clean Architecture + Vertical Slices under ADR-0011's "per-service pilot exception" umbrella. ApiGateway is the **ninth and final** pilot.

Production code surface today:
- `Gateway/GatewayProvider.cs`, `Gateway/GatewayProviderExtensions.cs`, `Gateway/GatewayProviderOptions.cs` — provider switch (`Yarp` default, `Ocelot` fallback per ADR-0001).
- `Gateway/YarpGatewayModule.cs`, `Gateway/OcelotGatewayModule.cs` — provider modules.
- `Gateway/SwaggerAggregation/{GatewayRouteDiscovery,GatewayRouteInfo,GatewaySpecTransformer,SwaggerAggregationModule}.cs` — aggregated Swagger UI.
- `Operator/OperatorModule.cs` — five operator HTTP routes + shared DI + helper record DTOs (`DeadLetterDetailResponse`, `DiscardRequest`, `BatchReplayRequest`, `BatchReplayItem`, `BatchReplayResponse`) + private `internal static class JwtClaimTypes` at the bottom.
- `Operator/OutboxPolling/{OutboxFailurePoller,OutboxFailureClient,OutboxFailureItem,OutboxPollerOptions}.cs` — hosted poller that ingests `/internal/outbox/failed` rows into the gateway-owned DLQ store.
- `Program.cs` — calls `builder.AddConfiguredGateway()`, `OperatorModule.AddServices(builder)`, then `OperatorModule.MapEndpoints(app)`, plus `AddJwtAuthentication`, `AddPlatformObservability`, `AddPlatformHealthChecks`, `app.ApplyDeadLetterMigrations()` in Dev.

Test surface today:
- `ApiGateway.Tests/Gateway/GatewayProviderExtensionsTests.cs`, `Gateway/SwaggerAggregation/GatewaySpecTransformerTests.cs`.
- `ApiGateway.Tests/Operator/{DeadLetterDetailEndpointTests,DeadLetterDiscardEndpointTests,DeadLetterReplayBatchEndpointTests,OperatorEndpointsRoutingTests,OutboxFailurePollerTests,OperatorMessagingProviderTests}.cs`.
- `ApiGateway.Tests/Integration/{GatewayIntegrationTests,GatewayWebApplicationFactory,StubHttpServer,SwaggerAggregationIntegrationTests,SwaggerStubServer}.cs` — `WebApplicationFactory<Program>`-based boot tests.

Pilot #9 (after Order / Product / Basket / Auth / Inventory / Shipping / Payment / Saga). Ninth and final pilot — every service in the monorepo on VSA after this lands. Zero functional behavior change. Boundaries enforced twice (NetArchTest + Roslyn analyzer). No `Domain/` (gateway owns no aggregate — `DeadLetterMessage` lives in `ECommerce.Shared.Infrastructure.DeadLetter`). No `Contracts/Integration/` (gateway publishes no integration events).

## Architectural decisions

Durable decisions that apply across all phases:

- **Project shape**: single `ApiGateway.csproj` retained; boundaries enforced by namespace + Roslyn analyzer + NetArchTest, not csproj split.
- **Folder topology**:
  - `Features/Operator/<EndpointSlice>/` — one slice per operator HTTP endpoint. Self-contained: `Endpoint.cs`, `Handler.cs`, slice-local request/response DTOs, slice DI extension.
    - `ListFailures/`, `GetFailureDetail/`, `ReplayFailure/`, `DiscardFailure/`, `BatchReplayFailures/`.
  - `Infrastructure/Proxy/` — `Yarp/YarpGatewayModule.cs`, `Ocelot/OcelotGatewayModule.cs`, `SwaggerAggregation/{GatewayRouteDiscovery,GatewayRouteInfo,GatewaySpecTransformer,SwaggerAggregationModule}.cs`, plus the three provider-switch files (`GatewayProvider.cs`, `GatewayProviderExtensions.cs`, `GatewayProviderOptions.cs`) at the `Infrastructure/Proxy/` root.
  - `Infrastructure/Polling/` — `OutboxFailurePoller.cs`, `OutboxFailureClient.cs`, `OutboxFailureItem.cs`, `OutboxPollerOptions.cs`.
  - `Infrastructure/Auth/JwtClaimTypes.cs` — `internal static class` lifted out of the bottom of `OperatorModule.cs`. Not promoted to `ECommerce.Shared` (out of scope per PRD).
  - **No `Domain/`** — gateway owns no aggregate. `DeadLetterMessage`, `DeadLetterStatus`, `DeadLetterOrigin`, `DeadLetterFilter`, `IDeadLetterStore`, `IDeadLetterReplayer`, `IDeadLetterDiscarder` continue to live in `ECommerce.Shared.Infrastructure.DeadLetter`.
  - **No `Contracts/Integration/`** — gateway publishes no integration events. It consumes failed outbox HTTP feeds and persists to the gateway-owned `DeadLetterDbContext`.
- **Namespaces**: `ApiGateway.Features.Operator.<EndpointSlice>`, `ApiGateway.Infrastructure.Proxy`, `ApiGateway.Infrastructure.Proxy.Yarp`, `ApiGateway.Infrastructure.Proxy.Ocelot`, `ApiGateway.Infrastructure.Proxy.SwaggerAggregation`, `ApiGateway.Infrastructure.Polling`, `ApiGateway.Infrastructure.Auth`. The `ApiGateway.Gateway`, `ApiGateway.Gateway.SwaggerAggregation`, `ApiGateway.Operator`, `ApiGateway.Operator.OutboxPolling` namespaces are retired.
- **HTTP routes**: unchanged — `GET /operator/api/failures`, `GET /operator/api/failures/{id}`, `POST /operator/api/failures/{id}/replay`, `POST /operator/api/failures/{id}/discard`, `POST /operator/api/failures/replay-batch`; all gated by `AuthorizationPolicies.RequireOperatorPolicy`. `/health/{live,ready}`, `/metrics`, all proxy-managed routes preserved byte-for-byte.
- **Schema**: unchanged. Gateway-owned `dead_letter_messages` table preserved; `app.ApplyDeadLetterMigrations()` in Dev still runs from `Program.cs`.
- **Configuration shape**: unchanged. `Gateway:Provider` (`Yarp` default / `Ocelot` / unknown → fail fast), `Gateway:ClusterAddresses:*`, `ReverseProxy:Clusters:*:Destinations:default:Address`, `Operator:OutboxPolling:{Enabled,IntervalSeconds,Services[]}`, `Operator:TraceUiBaseUrl`, `Authentication:*`.
- **Auth**: `RequireOperatorPolicy` (Bearer + `Operator` claim) enforced on every `/operator/api/failures*` route — verbatim. Service-token policy (`RequireService`) unchanged. `JwtClaimTypes.Subject = "sub"` literal moved to `Infrastructure/Auth/JwtClaimTypes.cs` (still `internal static`, gateway-only).
- **Provider switch**: `AddConfiguredGateway` / `UseConfiguredGatewayAsync` extension surface and call-site keep their current shape; only namespace moves to `ApiGateway.Infrastructure.Proxy`. Unknown `Gateway:Provider` values continue to fail fast with the existing error message.
- **Slice DI**: each slice exposes `AddXxxSlice(this IServiceCollection)` + `MapXxxSlice(this IEndpointRouteBuilder)` extensions, called from `Program.cs`. The current `OperatorModule.AddServices` / `MapEndpoints` god-methods are deleted; cross-cutting shared DI calls (`AddPlatformEventBus`, `AddDeadLetter`, `AddRequireOperatorPolicy`) move directly into `Program.cs` because they are not slice-specific.
- **Helper-record DTOs**: `DeadLetterDetailResponse` migrates into `Features/Operator/GetFailureDetail/`. `DiscardRequest` migrates into `Features/Operator/DiscardFailure/`. `BatchReplayRequest`, `BatchReplayItem`, `BatchReplayResponse` migrate into `Features/Operator/BatchReplayFailures/`. Per-slice slice-local DTO ownership — no `ApiModels/` peer folder.
- **No outbox seam**: gateway has no `IIntegrationMap<,>` + `DomainEventOutboxInterceptor` because gateway has no aggregate and publishes no integration events. Matches Inventory/Shipping/Saga/Basket; diverges from Order/Payment.
- **Hosted poller as Infrastructure plumbing**: `OutboxFailurePoller` stays a hosted `BackgroundService`, not a `Features/` slice — same rationale as Shipping's `Infrastructure/Carriers/CarrierPollingService` and Saga's `Infrastructure/Reaper/SagaReaperService`. Internal scheduling, no inbound trigger.
- **Cross-slice rule**: NetArchTest forbids `Features.<X>.*` → `Features.<Y>.*` source references for distinct slices. `Infrastructure.*` cannot reference `Features.*`. `Features.*` may reference `Infrastructure.*` and `ECommerce.Shared.*`.
- **Layout assertions specific to the gateway** (in addition to the generic VSA rules):
  - `Domain/` folder does **not** exist (asserted explicitly).
  - `Contracts/` folder does **not** exist (asserted explicitly).
  - Top-level `Endpoints/`, `ApiModels/`, `Models/`, `Gateway/`, `Operator/` folders do **not** exist (legacy layout artifacts).
- **Composition tests**: cross-slice DI boot + routing tests (`OperatorEndpointsRoutingTests`, `OperatorMessagingProviderTests`) reclassified to `ApiGateway.Tests/Composition/` (one folder, not per-slice) because they exercise boot-time wiring, not slice behavior. Mirrors saga's pattern for composition-level tests.
- **Integration tests**: `ApiGateway.Tests/Integration/{GatewayIntegrationTests,GatewayWebApplicationFactory,StubHttpServer,SwaggerAggregationIntegrationTests,SwaggerStubServer}.cs` stay at project root unchanged. Boot-end integration tests stay framework-style, not slice-style. `public partial class Program { }` at the bottom of `Program.cs` preserved verbatim.
- **Divergences from prior pilots** to honor:
  1. **No `Domain/` folder.** Gateway owns no aggregate. Asserted by NetArchTest + analyzer.
  2. **No `Contracts/Integration/` folder.** Gateway publishes no integration events. Asserted by NetArchTest + analyzer.
  3. **No outbox seam.** Matches Inventory/Shipping/Saga/Basket; diverges from Order/Payment.
  4. **Proxy plumbing under `Infrastructure/Proxy/`.** No `Features/Proxy/` slice — proxy has no inbound user trigger; it is internal HTTP plumbing. Mirrors Shipping's `Infrastructure/Carriers/` decision.
  5. **Hosted DLQ poller under `Infrastructure/Polling/`.** Mirrors Saga's `Infrastructure/Reaper/` and Shipping's `Infrastructure/Carriers/CarrierPollingService` decisions.
  6. **Ninth and final pilot.** Closes out the migration — every service in the monorepo on VSA after this lands. Companion ADR promotes the convention from "per-service pilot exception" to "default service shape" and supersedes the per-service exception sections of ADR-0011.
- **Composition**: files **new** ADR (not a "compose 0011 by reference" pilot, unlike pilots 2–8). The new ADR records the promotion to default service shape and lists permitted divergences (Auth no `Contracts/`, Gateway no `Contracts/`+no `Domain/`, Basket/Inventory/Shipping/Saga no outbox seam, Payment multi-producer slice convention, Saga two-level nesting + dual subscription). Reuses [adding-a-new-slice.md](../runbooks/adding-a-new-slice.md) runbook unchanged.
- **`Program.cs`**: stays at project root. After Phase 9 it reads as a manifest — chained `AddXxxSlice()` registration block + `MapXxxSlice()` mapping block + cross-cutting `AddPlatformEventBus`, `AddDeadLetter`, `AddRequireOperatorPolicy`, `AddJwtAuthentication`, `AddPlatformObservability`, `AddPlatformHealthChecks`, `AddConfiguredGateway`/`UseConfiguredGatewayAsync`, `app.ApplyDeadLetterMigrations()` in Dev. The `public partial class Program { }` declaration preserved so `WebApplicationFactory<Program>` continues to work.
- **Rollout**: 12 staged commits on `refactor/api-gateway-vsa`, each green. Single PR for review. Pre-commit hook gates every commit (no `--no-verify`, no validation deferral).
- **Critical files to modify**:
  - `api-gateway/ApiGateway/Operator/OperatorModule.cs` (dissolved by Phase 8 — body migrated into 5 slices, file deleted)
  - `api-gateway/ApiGateway/Operator/OutboxPolling/*` (relocated Phase 4)
  - `api-gateway/ApiGateway/Gateway/{GatewayProvider,GatewayProviderExtensions,GatewayProviderOptions,YarpGatewayModule,OcelotGatewayModule}.cs` (relocated Phase 3)
  - `api-gateway/ApiGateway/Gateway/SwaggerAggregation/*.cs` (relocated Phase 3)
  - `api-gateway/ApiGateway/Program.cs` (becomes slice manifest by Phase 9)
  - `api-gateway/ApiGateway.Tests/Operator/*` (split per-slice + composition Phase 10)
  - `api-gateway/ApiGateway.Tests/Gateway/*` (relocated Phase 10)
  - `api-gateway/ApiGateway.Tests/Integration/*` (kept verbatim)
  - root `CLAUDE.md` (sweep Phase 12 — eight exception paragraphs collapse to one default-shape paragraph)
  - `docs/adr/00XX-clean-arch-vsa-default-service-shape.md` (new ADR Phase 12)
- **Critical files to copy/mirror** (prior pilots, do not modify):
  - `saga-microservice/Saga.Tests/Architecture/LayoutTests.cs` — closest prior-art NetArchTest layout (most recent pilot)
  - `saga-microservice/Saga.Tests/Architecture/LayoutAnalyzerTests.cs` — analyzer test shape
  - `saga-microservice/Saga.Service.LayoutAnalyzer/LayoutAnalyzer.cs` — analyzer skeleton + diagnostic IDs (rename `SAGLAY***` → `AGWLAY***`)
  - `auth-microservice/Auth.Tests/Architecture/LayoutTests.cs` — prior art for the no-`Contracts/` assertion (gateway extends this to also assert no `Domain/`)
  - `shipping-microservice/Shipping.Service/Infrastructure/Carriers/CarrierPollingService.cs` — prior art for hosted-service-in-`Infrastructure/` shape (poller mirror)
  - `saga-microservice/Saga.Service/Program.cs` — slice-manifest shape (most recent pilot)
  - `inventory-microservice/Inventory.Service/Features/<Slice>/<Slice>SliceExtensions.cs` — slice DI extension shape from a pilot that also skipped the outbox seam

---

## Phase 1: Scaffold NetArchTest + LayoutAnalyzer (rules off)

**User stories**: 4, 5.

### What to build

Add new `ApiGateway.Service.LayoutAnalyzer` csproj (copy Saga analyzer skeleton, rename diagnostic IDs `SAGLAY***` → `AGWLAY***`, rules empty / disabled). Wire as `Analyzer` ProjectReference from `ApiGateway.csproj`. Add `ApiGateway.Tests/Architecture/LayoutTests.cs` + `ApiGateway.Tests/Architecture/LayoutAnalyzerTests.cs` with every test marked `[Fact(Skip="enabled in Phase 11")]`. No production code changes.

### Acceptance criteria

- [ ] `dotnet build api-gateway` green
- [ ] `dotnet test api-gateway/ApiGateway.Tests` green (skipped tests count > 0)
- [ ] `dotnet format --verify-no-changes` green
- [ ] Commit: `refactor(api-gateway): Phase 1 scaffold NetArchTest + LayoutAnalyzer`

---

## Phase 2: Lift `JwtClaimTypes` to `Infrastructure/Auth/`

**User stories**: 19.

### What to build

Move the `internal static class JwtClaimTypes { public const string Subject = "sub"; }` from the bottom of `Operator/OperatorModule.cs` to a new file `Infrastructure/Auth/JwtClaimTypes.cs` under namespace `ApiGateway.Infrastructure.Auth`. Add `using ApiGateway.Infrastructure.Auth;` to `OperatorModule.cs` (callers still resolve `JwtClaimTypes.Subject` verbatim). Smallest possible standalone phase — proves the `Infrastructure/Auth/` namespace lands cleanly before larger moves.

### Acceptance criteria

- [ ] Build green
- [ ] `dotnet test api-gateway/ApiGateway.Tests` green
- [ ] `dotnet format --verify-no-changes` green
- [ ] `Infrastructure/Auth/JwtClaimTypes.cs` present; `OperatorModule.cs` no longer contains the `JwtClaimTypes` declaration
- [ ] Commit: `refactor(api-gateway): Phase 2 lift JwtClaimTypes to Infrastructure/Auth/`

---

## Phase 3: Relocate proxy plumbing to `Infrastructure/Proxy/`

**User stories**: 6, 13, 14, 15, 16.

### What to build

Move all proxy/Swagger-aggregation files into `Infrastructure/Proxy/` under three sub-folders + three root files:

- `Infrastructure/Proxy/Yarp/YarpGatewayModule.cs` ← `Gateway/YarpGatewayModule.cs`. Namespace `ApiGateway.Infrastructure.Proxy.Yarp`.
- `Infrastructure/Proxy/Ocelot/OcelotGatewayModule.cs` ← `Gateway/OcelotGatewayModule.cs`. Namespace `ApiGateway.Infrastructure.Proxy.Ocelot`.
- `Infrastructure/Proxy/SwaggerAggregation/{GatewayRouteDiscovery,GatewayRouteInfo,GatewaySpecTransformer,SwaggerAggregationModule}.cs` ← `Gateway/SwaggerAggregation/*`. Namespace `ApiGateway.Infrastructure.Proxy.SwaggerAggregation`.
- `Infrastructure/Proxy/{GatewayProvider,GatewayProviderExtensions,GatewayProviderOptions}.cs` ← `Gateway/{GatewayProvider,GatewayProviderExtensions,GatewayProviderOptions}.cs`. Namespace `ApiGateway.Infrastructure.Proxy`.

Pure relocation + namespace rename. Public extension surface (`AddConfiguredGateway`, `UseConfiguredGatewayAsync`, `AddSwaggerAggregation`, `UseSwaggerAggregation`) unchanged. `Program.cs` `using ApiGateway.Gateway;` → `using ApiGateway.Infrastructure.Proxy;`. `YarpGatewayModule.AddServices(builder)` / `OcelotGatewayModule.AddServices(builder)` / `OcelotGatewayModule.UseMiddlewareAsync(app)` / `YarpGatewayModule.UseMiddleware(app)` call shapes unchanged. Delete the empty `Gateway/` + `Gateway/SwaggerAggregation/` folders.

Update test `using`s in `ApiGateway.Tests/Gateway/GatewayProviderExtensionsTests.cs` + `Gateway/SwaggerAggregation/GatewaySpecTransformerTests.cs` (file relocations happen Phase 10; only namespace updates here).

### Acceptance criteria

- [ ] Build green
- [ ] `dotnet test api-gateway/ApiGateway.Tests` green — `GatewayProviderExtensionsTests`, `GatewaySpecTransformerTests`, `GatewayIntegrationTests`, `SwaggerAggregationIntegrationTests` all pass
- [ ] `Gateway/` folder + `Gateway/SwaggerAggregation/` folder deleted
- [ ] `Infrastructure/Proxy/{Yarp,Ocelot,SwaggerAggregation}/` populated; three provider-switch files at `Infrastructure/Proxy/` root
- [ ] `Gateway:Provider=Yarp` and `Gateway:Provider=Ocelot` boot identical to today (verified by `OperatorMessagingProviderTests` if it covers provider switching, plus `GatewayIntegrationTests`)
- [ ] Unknown `Gateway:Provider` value still fails fast with existing error message
- [ ] Commit: `refactor(api-gateway): Phase 3 relocate proxy plumbing to Infrastructure/Proxy/`

---

## Phase 4: Relocate hosted DLQ poller to `Infrastructure/Polling/`

**User stories**: 7, 21.

### What to build

Move the four poller files from `Operator/OutboxPolling/` to `Infrastructure/Polling/` under namespace `ApiGateway.Infrastructure.Polling`:

- `OutboxFailurePoller.cs` (hosted `BackgroundService`)
- `OutboxFailureClient.cs` (`IOutboxFailureClient` + `OutboxFailureClient` impl)
- `OutboxFailureItem.cs`
- `OutboxPollerOptions.cs`

Pure relocation + namespace rename. `OperatorModule.AddServices` `using ApiGateway.Operator.OutboxPolling;` → `using ApiGateway.Infrastructure.Polling;`. DI registration shape preserved (`AddHttpClient<IOutboxFailureClient, OutboxFailureClient>()` + `AddHostedService<OutboxFailurePoller>()` + `AddSingleton(pollerOptions)`, all gated on `pollerOptions.Enabled`). Configuration section name (`OutboxPollerOptions.SectionName` → `Operator:OutboxPolling`) unchanged.

Update test `using` in `ApiGateway.Tests/Operator/OutboxFailurePollerTests.cs` (file relocation happens Phase 10).

Delete the empty `Operator/OutboxPolling/` folder.

### Acceptance criteria

- [ ] Build green
- [ ] `dotnet test api-gateway/ApiGateway.Tests` green — `OutboxFailurePollerTests` passes
- [ ] `Operator/OutboxPolling/` folder deleted
- [ ] `Infrastructure/Polling/` contains the four files
- [ ] Poller continues to upsert with existing-vs-new branching on `Origin == Outbox && Service == serviceName && Id == failureId` (verified by `OutboxFailurePollerTests`)
- [ ] Commit: `refactor(api-gateway): Phase 4 relocate DLQ poller to Infrastructure/Polling/`

---

## Phase 5: Carve `ListFailures` slice (first of five)

**User stories**: 1, 2, 8.

### What to build

Create `Features/Operator/ListFailures/` with:
- `Endpoint.cs` — Minimal API delegate registering `GET /operator/api/failures` against the per-slice `MapListFailuresSlice` extension. Preserves group prefix + `RequireAuthorization(AuthorizationPolicies.RequireOperatorPolicy)`.
- `Handler.cs` — `internal sealed class ListFailuresHandler`. Constructor injects `IDeadLetterStore`. Builds `DeadLetterFilter` from query params (`service`, `eventType`, `status`, `from`, `to`, `origin`, `page`, `pageSize`), calls `store.ListAsync(filter, ct)`, returns the raw paged result. Response shape unchanged.
- `ListFailuresSliceExtensions.cs` — exposes `AddListFailuresSlice(this IServiceCollection)` (registers handler `AddScoped`) + `MapListFailuresSlice(this IEndpointRouteBuilder, RouteGroupBuilder group)`. Namespace `ApiGateway.Features.Operator.ListFailures`.

Wire into `Program.cs`: build the operator group once (`var operatorGroup = app.MapGroup("/operator/api/failures").RequireAuthorization(AuthorizationPolicies.RequireOperatorPolicy);`) and call `operatorGroup.MapListFailuresSlice()`. Add `builder.Services.AddListFailuresSlice()` to the service-registration block. Delete the corresponding `group.MapGet("/", ...)` lambda from `OperatorModule.MapEndpoints`. `OperatorModule` shrinks but remains alive (Phases 6–8 finish it).

### Acceptance criteria

- [ ] Build green
- [ ] `dotnet test api-gateway/ApiGateway.Tests` green — `OperatorEndpointsRoutingTests` list-route coverage continues passing; existing list-endpoint tests pass against the relocated handler
- [ ] `Features/Operator/ListFailures/` contains `Endpoint.cs` + `Handler.cs` + `ListFailuresSliceExtensions.cs`
- [ ] `OperatorModule.MapEndpoints` no longer maps `GET /`
- [ ] Response shape, filters, pagination unchanged (Bruno-equivalent smoke OK)
- [ ] Commit: `refactor(api-gateway): Phase 5 carve ListFailures slice`

---

## Phase 6: Carve `GetFailureDetail` slice

**User stories**: 1, 2, 9.

### What to build

Create `Features/Operator/GetFailureDetail/` with:
- `Endpoint.cs` — `GET /operator/api/failures/{id:guid}` against `MapGetFailureDetailSlice`. Same group + auth policy.
- `Handler.cs` — `internal sealed class GetFailureDetailHandler`. Constructor injects `IDeadLetterStore` + `IConfiguration`. Body migrated from `OperatorModule.GetFailureDetail` (lookup → 404 if null → compute optional `TraceUrl` from `Operator:TraceUiBaseUrl` and `message.CorrelationId` → return `Ok(new DeadLetterDetailResponse(message, traceUrl))`).
- `DeadLetterDetailResponse.cs` — slice-local record (migrated from bottom of `OperatorModule.cs`). Namespace `ApiGateway.Features.Operator.GetFailureDetail`.
- `GetFailureDetailSliceExtensions.cs` — `AddGetFailureDetailSlice` + `MapGetFailureDetailSlice` extensions.

Wire into `Program.cs`: `builder.Services.AddGetFailureDetailSlice()` + `operatorGroup.MapGetFailureDetailSlice()`. Delete the corresponding `group.MapGet("/{id:guid}", ...)` lambda + the `OperatorModule.GetFailureDetail` static method + the `DeadLetterDetailResponse` record from `OperatorModule.cs`.

Update `ApiGateway.Tests/Operator/DeadLetterDetailEndpointTests.cs` `using` for `DeadLetterDetailResponse` (file relocation Phase 10).

### Acceptance criteria

- [ ] Build green
- [ ] `dotnet test api-gateway/ApiGateway.Tests` green — `DeadLetterDetailEndpointTests` continues passing (TraceUrl shape unchanged, 404 path unchanged)
- [ ] `Features/Operator/GetFailureDetail/` contains `Endpoint.cs` + `Handler.cs` + `DeadLetterDetailResponse.cs` + `GetFailureDetailSliceExtensions.cs`
- [ ] `OperatorModule.cs` no longer declares `DeadLetterDetailResponse` or `GetFailureDetail` static
- [ ] Commit: `refactor(api-gateway): Phase 6 carve GetFailureDetail slice`

---

## Phase 7: Carve `ReplayFailure` + `DiscardFailure` slices

**User stories**: 1, 2, 3, 10, 11, 18.

### What to build

Two slices in one commit (each ~1 endpoint + small DTO, same shape):

`Features/Operator/ReplayFailure/`:
- `Endpoint.cs` — `POST /operator/api/failures/{id:guid}/replay`.
- `Handler.cs` — `internal sealed class ReplayFailureHandler`. Constructor injects `IDeadLetterReplayer`. Resolves `replayedBy` from `ClaimsPrincipal` (`JwtClaimTypes.Subject` from `ApiGateway.Infrastructure.Auth` → fallback to `ClaimTypes.NameIdentifier` → `Identity?.Name` → `"unknown"`), calls `replayer.ReplayAsync(id, replayedBy, ct)`, maps outcome → HTTP (Success → 202 with `{id, newMessageId}`, NotFound → 404, NotPending → 409, PublishFailed/default → 502 Problem). Mapping byte-identical to current `OperatorModule`.
- `ReplayFailureSliceExtensions.cs` — `AddReplayFailureSlice` + `MapReplayFailureSlice`.

`Features/Operator/DiscardFailure/`:
- `Endpoint.cs` — `POST /operator/api/failures/{id:guid}/discard`.
- `Handler.cs` — `internal sealed class DiscardFailureHandler`. Constructor injects `IDeadLetterDiscarder`. Resolves `discardedBy` same way as `ReplayFailureHandler`. Validates `request?.Reason` non-empty (returns 400 with `{id, error="discard reason is required"}` if missing). Calls `discarder.DiscardAsync(id, discardedBy, reason, ct)`, maps outcome → HTTP (Success → 202, NotFound → 404, NotPending → 409, ReasonRequired → 400, default → 500 Problem). Mapping byte-identical.
- `DiscardRequest.cs` — slice-local record (migrated from `OperatorModule.cs`). Namespace `ApiGateway.Features.Operator.DiscardFailure`.
- `DiscardFailureSliceExtensions.cs` — `AddDiscardFailureSlice` + `MapDiscardFailureSlice`.

Wire both into `Program.cs`. Delete the two `group.MapPost(...)` lambdas + `OperatorModule.DiscardFailure` static + `DiscardRequest` record from `OperatorModule.cs`.

Update test `using`s in `ApiGateway.Tests/Operator/DeadLetterDiscardEndpointTests.cs` for `DiscardRequest`.

### Acceptance criteria

- [ ] Build green
- [ ] `dotnet test api-gateway/ApiGateway.Tests` green — `DeadLetterDiscardEndpointTests` passes; replay-endpoint tests (in `OperatorEndpointsRoutingTests` and any dedicated replay tests) pass; outcome → HTTP mapping byte-identical
- [ ] `Features/Operator/ReplayFailure/` + `Features/Operator/DiscardFailure/` populated
- [ ] `OperatorModule.cs` no longer declares `DiscardRequest`, `DiscardFailure`, or maps the replay/discard routes
- [ ] `JwtClaimTypes.Subject` resolution unchanged (still falls back through `NameIdentifier` → `Identity?.Name` → `"unknown"`)
- [ ] Commit: `refactor(api-gateway): Phase 7 carve ReplayFailure + DiscardFailure slices`

---

## Phase 8: Carve `BatchReplayFailures` slice; delete `OperatorModule.cs`

**User stories**: 1, 2, 12.

### What to build

`Features/Operator/BatchReplayFailures/`:
- `Endpoint.cs` — `POST /operator/api/failures/replay-batch`.
- `Handler.cs` — `internal sealed class BatchReplayFailuresHandler`. Constructor injects `IDeadLetterReplayer`. Resolves `replayedBy` same way as `ReplayFailureHandler`. Validates `request?.Ids` non-null + non-empty (returns 400 with `{error="ids are required"}` if not). Loops over ids, calls `replayer.ReplayAsync` for each, maps outcome → per-item status string (`success|not_found|not_pending|publish_failed|unknown`) byte-identical to current `OperatorModule.BatchReplay`. Returns `Ok(new BatchReplayResponse(items))`.
- `BatchReplayRequest.cs`, `BatchReplayItem.cs`, `BatchReplayResponse.cs` — slice-local records migrated from `OperatorModule.cs`. Namespace `ApiGateway.Features.Operator.BatchReplayFailures`.
- `BatchReplayFailuresSliceExtensions.cs` — `AddBatchReplayFailuresSlice` + `MapBatchReplayFailuresSlice`.

Wire into `Program.cs`. Delete the last `group.MapPost("/replay-batch", ...)` lambda + `OperatorModule.BatchReplay` static + the three batch-record declarations + `OperatorModule.MapEndpoints` + `OperatorModule.AddServices` + `OperatorModule.OperatorPathPrefix` constant. Move shared DI calls that lived in `OperatorModule.AddServices` (`AddPlatformEventBus(builder.Configuration)`, `AddDeadLetter(builder.Configuration)`, `AddRequireOperatorPolicy()`, and the `OutboxPollerOptions` binding + conditional poller registration) directly into `Program.cs` because they are cross-cutting, not slice-specific.

**Delete `Operator/OperatorModule.cs`.** Delete the empty `Operator/` folder.

Update test `using`s in `ApiGateway.Tests/Operator/DeadLetterReplayBatchEndpointTests.cs` for `BatchReplayRequest`/`BatchReplayItem`/`BatchReplayResponse`.

### Acceptance criteria

- [ ] Build green
- [ ] `dotnet test api-gateway/ApiGateway.Tests` green — `DeadLetterReplayBatchEndpointTests` passes with byte-identical per-item status mapping; `OperatorEndpointsRoutingTests` passes for all 5 routes
- [ ] **`Operator/OperatorModule.cs` deleted**
- [ ] **`Operator/` folder deleted**
- [ ] `Features/Operator/{ListFailures,GetFailureDetail,ReplayFailure,DiscardFailure,BatchReplayFailures}/` all populated
- [ ] `Program.cs` no longer references `OperatorModule`
- [ ] Commit: `refactor(api-gateway): Phase 8 carve BatchReplayFailures + delete OperatorModule`

---

## Phase 9: Reshape `Program.cs` into slice manifest

**User stories**: 23 (preparatory), 25, 26, 27.

### What to build

Reshape `Program.cs` into a clean manifest:

1. **Service-registration block** (in order):
   - Shared infra: `builder.AddConfiguredGateway()`, `builder.Services.AddPlatformEventBus(builder.Configuration)`, `builder.Services.AddDeadLetter(builder.Configuration)`, `builder.Services.AddRequireOperatorPolicy()`, `builder.Services.AddJwtAuthentication(builder.Configuration)`, `builder.AddPlatformObservability("ApiGateway", customTracing: tracing => tracing.AddSource("Yarp.ReverseProxy"))`, `builder.Services.AddPlatformHealthChecks()`.
   - DLQ poller registration (lift from old `OperatorModule.AddServices`): bind `OutboxPollerOptions`, register singleton, conditionally `AddHttpClient<IOutboxFailureClient, OutboxFailureClient>()` + `AddHostedService<OutboxFailurePoller>()` if `Enabled`.
   - Slice registrations (chained): `builder.Services.AddListFailuresSlice().AddGetFailureDetailSlice().AddReplayFailureSlice().AddDiscardFailureSlice().AddBatchReplayFailuresSlice()`.
2. **Pipeline block** (in order):
   - Dev-only `app.ApplyDeadLetterMigrations()`.
   - `app.UsePrometheusExporter()`, `app.MapPlatformHealthChecks()`, `app.UseJwtAuthentication()`.
   - Build operator group once: `var operatorGroup = app.MapGroup("/operator/api/failures").RequireAuthorization(AuthorizationPolicies.RequireOperatorPolicy);`.
   - Slice mappings (chained or sequential): `operatorGroup.MapListFailuresSlice(); operatorGroup.MapGetFailureDetailSlice(); operatorGroup.MapReplayFailureSlice(); operatorGroup.MapDiscardFailureSlice(); operatorGroup.MapBatchReplayFailuresSlice();`.
   - `await app.UseConfiguredGatewayAsync()`.
3. **`public partial class Program { }`** preserved at the bottom — `WebApplicationFactory<Program>` boot tests depend on this.

Zero per-handler `AddScoped<...Handler>` calls in `Program.cs` (all in slice extensions). Zero per-route `Map*` lambdas in `Program.cs` (all in slice `Endpoint.cs` files). The `AddRequireServicePolicy` line is **not** needed (no `/internal/*` route lives in gateway; only operator routes).

### Acceptance criteria

- [ ] Build green
- [ ] Full `dotnet test api-gateway/ApiGateway.Tests` green — `GatewayIntegrationTests`, `SwaggerAggregationIntegrationTests`, `OperatorEndpointsRoutingTests`, all 5 endpoint tests, `OutboxFailurePollerTests`, `OperatorMessagingProviderTests` all pass
- [ ] `Program.cs` zero per-handler `AddScoped<...Handler>` calls
- [ ] `Program.cs` zero `group.Map*(...)` lambdas
- [ ] `Program.cs` reads as manifest (~7 shared-infra lines + 1 poller block + 5 `AddXxxSlice()` + 5 `MapXxxSlice()` + `UseConfiguredGatewayAsync`)
- [ ] `public partial class Program { }` preserved
- [ ] Commit: `refactor(api-gateway): Phase 9 reshape Program.cs into slice manifest`

---

## Phase 10: Reshape `ApiGateway.Tests` to mirror Features/ + Infrastructure/

**User stories**: 20, 21, 22.

### What to build

Move existing test classes per PRD Testing Decisions:

- `ApiGateway.Tests/Operator/DeadLetterDetailEndpointTests.cs` → `ApiGateway.Tests/Features/Operator/GetFailureDetail/DeadLetterDetailEndpointTests.cs`. Namespace touched only.
- `ApiGateway.Tests/Operator/DeadLetterDiscardEndpointTests.cs` → `ApiGateway.Tests/Features/Operator/DiscardFailure/DeadLetterDiscardEndpointTests.cs`.
- `ApiGateway.Tests/Operator/DeadLetterReplayBatchEndpointTests.cs` → `ApiGateway.Tests/Features/Operator/BatchReplayFailures/DeadLetterReplayBatchEndpointTests.cs`.
- Any list-endpoint test class (if it exists today, otherwise covered by `OperatorEndpointsRoutingTests`) → `ApiGateway.Tests/Features/Operator/ListFailures/`. Any replay-endpoint test class → `ApiGateway.Tests/Features/Operator/ReplayFailure/`.
- `ApiGateway.Tests/Operator/OperatorEndpointsRoutingTests.cs` → `ApiGateway.Tests/Composition/OperatorEndpointsRoutingTests.cs`. Tests cross-slice routing (covers all 5 routes); not per-slice behavior. Namespace `ApiGateway.Tests.Composition`.
- `ApiGateway.Tests/Operator/OperatorMessagingProviderTests.cs` → `ApiGateway.Tests/Composition/OperatorMessagingProviderTests.cs`. Tests DI boot wiring across providers.
- `ApiGateway.Tests/Operator/OutboxFailurePollerTests.cs` → `ApiGateway.Tests/Infrastructure/Polling/OutboxFailurePollerTests.cs`. Namespace `ApiGateway.Tests.Infrastructure.Polling`.
- `ApiGateway.Tests/Gateway/GatewayProviderExtensionsTests.cs` → split into `ApiGateway.Tests/Infrastructure/Proxy/GatewayProviderExtensionsTests.cs` (`Yarp` / `Ocelot` / unknown-fails-fast assertions). Namespace `ApiGateway.Tests.Infrastructure.Proxy`.
- `ApiGateway.Tests/Gateway/SwaggerAggregation/GatewaySpecTransformerTests.cs` → `ApiGateway.Tests/Infrastructure/Proxy/SwaggerAggregation/GatewaySpecTransformerTests.cs`. Namespace `ApiGateway.Tests.Infrastructure.Proxy.SwaggerAggregation`.
- `ApiGateway.Tests/Integration/*` — **kept verbatim** at project root. `GatewayIntegrationTests`, `GatewayWebApplicationFactory`, `StubHttpServer`, `SwaggerAggregationIntegrationTests`, `SwaggerStubServer`. Boot-end framework-style tests stay framework-style; namespaces unchanged.

Delete the emptied `Operator/` test folder + the emptied `Gateway/` + `Gateway/SwaggerAggregation/` test folders.

### Acceptance criteria

- [ ] Build green
- [ ] Full `dotnet test api-gateway/ApiGateway.Tests` green — zero behavior diff on pre-existing tests
- [ ] `ApiGateway.Tests/Operator/` folder deleted
- [ ] `ApiGateway.Tests/Gateway/` folder deleted
- [ ] `ApiGateway.Tests/Features/Operator/{ListFailures,GetFailureDetail,ReplayFailure,DiscardFailure,BatchReplayFailures}/` populated
- [ ] `ApiGateway.Tests/Infrastructure/{Polling,Proxy,Proxy/SwaggerAggregation}/` populated
- [ ] `ApiGateway.Tests/Composition/` contains `OperatorEndpointsRoutingTests.cs` + `OperatorMessagingProviderTests.cs`
- [ ] `ApiGateway.Tests/Integration/` untouched
- [ ] Commit: `refactor(api-gateway): Phase 10 reshape ApiGateway.Tests into Features/ + Infrastructure/ + Composition/`

---

## Phase 11: Enable NetArchTest + LayoutAnalyzer rules

**User stories**: 4, 5.

### What to build

Unskip `LayoutTests.cs` + `LayoutAnalyzerTests.cs` (added in Phase 1 with `[Fact(Skip="enabled in Phase 11")]`). Fill in NetArchTest rules:

- `ApiGateway.Features.<X>.*` must not depend on `ApiGateway.Features.<Y>.*` for any distinct slice paths.
- `ApiGateway.Infrastructure.*` must not depend on `ApiGateway.Features.*`.
- `ApiGateway.Features.*` may depend on `ApiGateway.Infrastructure.*` and `ECommerce.Shared.*` only.
- Top-level folders `Endpoints/`, `ApiModels/`, `Models/`, `Gateway/`, `Operator/` must **not** exist (legacy layout artifacts; assertions ensure they never come back).
- **`Domain/` folder must not exist** (gateway-specific assertion; mirrors PRD Implementation Decisions note).
- **`Contracts/` folder must not exist** (gateway-specific assertion).

Promote `ApiGateway.Service.LayoutAnalyzer` diagnostics from hidden to error severity (`.editorconfig` or analyzer manifest). Fill in analyzer banned-namespace / banned-symbol diagnostics mirroring `Saga.Service.LayoutAnalyzer` with `AGWLAY***` IDs. Analyzer must also fire on the no-`Domain/` and no-`Contracts/` rules.

### Acceptance criteria

- [ ] `dotnet build api-gateway` green (analyzer doesn't fire on existing code — proves refactor satisfies rules)
- [ ] Full `dotnet test api-gateway/ApiGateway.Tests` green including all unskipped Architecture tests
- [ ] `LayoutAnalyzerTests.cs` proves each rule fires on synthetic violation input (slice cross-reference; `Infrastructure → Features` reference; synthetic `Domain/`; synthetic `Contracts/`)
- [ ] Commit: `refactor(api-gateway): Phase 11 enforce layout boundaries`

---

## Phase 12: New ADR "default service shape" + root `CLAUDE.md` sweep

**User stories**: 23, 24.

### What to build

Two doc artifacts, single commit:

1. **New ADR** `docs/adr/00XX-clean-arch-vsa-default-service-shape.md` (assign the next free number — likely `0012`). Status `Accepted`. Content:
   - Context: nine services migrated; ADR-0011 framed it as a "per-service pilot exception" with eight follow-up paragraphs in root `CLAUDE.md`. The exception list grew with every pilot. With api-gateway closing out the migration, every service in the monorepo is on Clean Architecture + Vertical Slices.
   - Decision: promote VSA from "per-service pilot exception" to **default service shape** for the monorepo. Supersedes the per-service exception sections of ADR-0011 (ADR-0011 itself remains in force as the Order-pilot decision record; only its propagation guidance is superseded).
   - Permitted divergences (recorded, not erased):
     - (a) `Auth` has no `Contracts/` folder (Auth produces and consumes no cross-service integration events).
     - (b) `ApiGateway` has neither `Contracts/` nor `Domain/` (gateway owns no aggregate and publishes no integration events).
     - (c) `Basket`, `Inventory`, `Shipping`, `Saga` have no `IIntegrationMap<,>` + `DomainEventOutboxInterceptor` outbox seam (no `Translate(...)` smell to dissolve in those services).
     - (d) `Payment` uses a multi-producer slice convention (HTTP and saga slices share a single `IIntegrationMap<,>` via DI; the saga slice raises the same domain event and the interceptor resolves the map globally — not a slice-to-slice source reference).
     - (e) `Saga` uses two-level `Features/<Saga>/<Trigger>/` nesting and a dual-subscription convention for `PaymentRefundedEvent` (two slices, each loads its own saga by id and no-ops if not its own).
   - Consequences: new services join the monorepo using the default shape; deviations require ADR amendment. The runbook `docs/runbooks/adding-a-new-slice.md` is now the canonical onboarding doc.
   - Supersedes / Composes: supersedes the "Propagation to remaining services is a separate ADR" footer present at the end of seven prior per-pilot exception blocks (those footers were forward-looking; they are now historical). Composes ADR [0011](0011-order-cleanarch-vsa-pilot.md) by reference.

2. **Root `CLAUDE.md` sweep**. Rewrite the `## Cross-service architecture` section so the eight per-service exception paragraphs collapse into **one default-shape paragraph** + **one short divergence list**. New shape (illustrative — exact wording chosen during the phase):

   > **Service layout (default).** Every service in the monorepo uses Clean Architecture + Vertical Slices: `Features/<Slice>/`, `Domain/`, `Contracts/Integration/`, `Infrastructure/`. Boundaries enforced per-service by NetArchTest (`<Service>.Tests/Architecture/LayoutTests.cs`) and a Roslyn `<Service>.Service.LayoutAnalyzer`. New slices follow the [adding-a-new-slice.md](docs/runbooks/adding-a-new-slice.md) runbook. ADRs: [0011](docs/adr/0011-order-cleanarch-vsa-pilot.md) (original pilot), [00XX](docs/adr/00XX-clean-arch-vsa-default-service-shape.md) (promoted to default).
   >
   > **Permitted divergences (recorded, not erased)**:
   > - **Auth**: no `Contracts/` folder (no cross-service integration events).
   > - **ApiGateway**: no `Contracts/` and no `Domain/` (no aggregate, no integration events).
   > - **Basket / Inventory / Shipping / Saga**: no `IIntegrationMap<,>` + `DomainEventOutboxInterceptor` outbox seam (no `Translate(...)` smell to dissolve).
   > - **Payment**: multi-producer slice convention — HTTP write slice and saga slice share a single `IIntegrationMap<,>` resolved globally via the outbox interceptor (not a slice-to-slice source reference).
   > - **Saga**: two-level `Features/<Saga>/<Trigger>/` nesting (two saga aggregates coexisting); dual-subscription for `PaymentRefundedEvent` (`Features/OrderSaga/PaymentRefunded/` and `Features/RefundSaga/PaymentRefunded/` both register, each loads its own saga by id and no-ops if not its own).

   Correct the saga paragraph's "eighth and final" claim: api-gateway closed out the migration as the ninth pilot. The new wording: "every service in the monorepo is on Clean Architecture + Vertical Slices; api-gateway closed out the migration."

   Delete the eight existing per-service exception paragraphs (Order, Product, Basket, Auth, Inventory, Shipping, Payment, Saga). The "Propagation to remaining services is a separate ADR" footer on seven of them is rendered historical by the new ADR.

No production code changes in this phase. No runbook changes.

### Acceptance criteria

- [ ] `docs/adr/00XX-clean-arch-vsa-default-service-shape.md` present with Status: Accepted
- [ ] `docs/adr/README.md` index updated to list the new ADR
- [ ] Root `CLAUDE.md` contains the new default-shape paragraph + divergence list; eight per-service exception paragraphs removed
- [ ] Saga paragraph's "eighth and final" claim corrected (api-gateway is the ninth and final pilot)
- [ ] Markdown links resolve (ADR 0011, new ADR, runbook)
- [ ] `dotnet format --verify-no-changes` green
- [ ] Commit: `refactor(api-gateway): Phase 12 new ADR + CLAUDE.md default-shape sweep`

---

## Verification (end-to-end, after Phase 12)

Run each from a clean `dotnet restore`:

1. **Format + build + test full ApiGateway stack**
   ```bash
   find api-gateway -type d \( -name bin -o -name obj \) -prune -exec rm -rf {} +
   cd api-gateway && dotnet restore && dotnet format --verify-no-changes && dotnet build && dotnet test
   ```
   Expected: all green.

2. **Pre-commit hook on the final commit**
   ```bash
   dotnet husky run --group pre-commit
   ```
   Expected: green (format + build + Basket tests).

3. **End-to-end stack smoke**
   ```bash
   docker compose up --build
   ```
   Then via Bruno/curl against `http://localhost:8004`:
   - `GET /health/live` → 200; `GET /health/ready` → 200; `GET /metrics` → Prometheus exposition format with `dlq_messages_total`, `dlq_replays_total`, `dlq_discards_total`.
   - With operator Bearer token: `GET /operator/api/failures` → 200 paged list; with filters `service=order&status=Pending&page=1&pageSize=10` → 200 filtered. With user (non-operator) token: 403.
   - `GET /operator/api/failures/{id}` (existing id) → 200 with `DeadLetterDetailResponse { Message, TraceUrl }` shape; `TraceUrl` populated when `Operator:TraceUiBaseUrl` set and `Message.CorrelationId` present. Unknown id → 404.
   - `POST /operator/api/failures/{id}/replay` → 202 with `{id, newMessageId}` (Success), 404 (NotFound), 409 (NotPending), 502 Problem (PublishFailed).
   - `POST /operator/api/failures/{id}/discard` with `{reason:"..."}` → 202; missing/empty `reason` → 400 with `{id, error:"discard reason is required"}`; 404 / 409 / 500 per outcome.
   - `POST /operator/api/failures/replay-batch` with `{ids:[...]}` → 200 with per-item `{id, status, newMessageId?, reason?}` array; status values exactly `success|not_found|not_pending|publish_failed|unknown`.
   - Proxy: `GET /products/...`, `GET /baskets/...`, etc. behave identically to today (YARP-backed by default).
   - Aggregated Swagger UI renders identically.

4. **Provider switch regression check**
   - Set `Gateway__Provider=Ocelot` in compose, restart, verify proxy routes still work identically.
   - Set `Gateway__Provider=Garbage`, restart, verify boot fails fast with the existing error message format.

5. **Cluster-address override check**
   - Set `Gateway__ClusterAddresses__Product=http://product-staging:8002` and confirm `ReverseProxy:Clusters:product-cluster:Destinations:default:Address` reflects the override at boot.

6. **DLQ poller behavior parity**
   - In a stack run, induce a poison message on Order's outbox path. Confirm gateway DLQ poller persists the row with `Origin=Outbox, Service=order, Id=<failureId>`. Re-poll: confirm upsert branch (existing row updated, not duplicated). Replay via `POST /operator/api/failures/{id}/replay`: confirm new message published to `OriginalQueue` with original `CorrelationId` propagated on the `dlq.replay` activity span.

7. **Boundary regression check**
   Add a deliberate violation locally (e.g. `Features/Operator/ListFailures/Handler.cs` adds `using ApiGateway.Features.Operator.ReplayFailure;`); confirm:
   - `dotnet build` fails with `AGWLAY***` analyzer diagnostic
   - `dotnet test ApiGateway.Tests --filter LayoutTests` fails the matching NetArchTest assertion
   Revert. Also try synthetic `Domain/Foo.cs` and synthetic `Contracts/Bar.cs`; both analyzer + NetArchTest fail. Revert.

8. **Reverse-grep regression check**
   Grep entire `api-gateway/ApiGateway/` for `OperatorModule`. Zero matches expected. Grep for `namespace ApiGateway.Gateway` and `namespace ApiGateway.Operator`. Zero matches expected.

9. **Telemetry parity**
   Hit Prometheus `/metrics` endpoint and confirm `dlq_messages_total`, `dlq_replays_total`, `dlq_discards_total` counters + `Yarp.ReverseProxy` activity source emit identical to pre-refactor.

10. **PR open + bisect spot-check**
    Open single PR `refactor/api-gateway-vsa` → `main`. `git bisect` any 3 random commits in the branch range and confirm each builds + tests green in isolation.

## Phases needing manual `dotnet test api-gateway/ApiGateway.Tests` before commit

Pre-commit hook only runs Basket tests. Run ApiGateway tests locally before staging on every phase, but pay especially close attention to:

- **Phase 3** — proxy plumbing relocation. `GatewayIntegrationTests`, `SwaggerAggregationIntegrationTests`, `GatewayProviderExtensionsTests` must all still pass; both `Yarp` and `Ocelot` provider boot paths must continue to work; unknown-provider fail-fast preserved.
- **Phase 4** — poller relocation. `OutboxFailurePollerTests` must continue to pass; upsert branching on `Origin == Outbox && Service == serviceName && Id == failureId` byte-identical.
- **Phases 5–8** — slice carve-outs. Each phase removes one or two routes from `OperatorModule.MapEndpoints` and reintroduces them through slice `Endpoint.cs` files. The corresponding endpoint test class must pass at every phase boundary. `OperatorEndpointsRoutingTests` (a routing smoke covering all 5 routes) must remain green at every phase boundary — it is the strongest regression signal during the carve-out.
- **Phase 9** — Program.cs reshape. `GatewayIntegrationTests` + `SwaggerAggregationIntegrationTests` boot via `WebApplicationFactory<Program>`; if `public partial class Program { }` or any `Add*Slice()` / `Map*Slice()` wiring goes missing, integration tests fail immediately.
- **Phase 11** — rule enablement (NetArchTest only fires under `dotnet test`; analyzer fires under `dotnet build` but skipped tests until now hid synthetic-violation coverage).

If hook fails with `MSB3248`: clean `bin`/`obj` → `dotnet restore --force` → rerun hook (per root `CLAUDE.md` sandbox policy). Do not `--no-verify`, do not defer validation. If still failing, **STOP and hand off to user — do not commit**.
