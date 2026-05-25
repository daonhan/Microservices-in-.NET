# PRD: API Gateway Clean Architecture + Vertical Slices pilot (ninth and final)

## Problem Statement

The api-gateway service is the last service in the monorepo still on the legacy "module" layout: two top-level folders (`Gateway/`, `Operator/`) each holding a static `*Module` god-class that mixes endpoint mapping, DI wiring, configuration binding, and helper records. Every other service (order, product, basket, auth, inventory, shipping, payment, saga) has already been migrated to Clean Architecture + Vertical Slices under ADR-0011's "per-service pilot exception" umbrella.

From a developer's perspective the inconsistency hurts navigation: every other service answers "where does this endpoint live?" with `Features/<Slice>/`, while api-gateway forces a scan of `OperatorModule.cs` and `MapEndpoints` to find the same code. The slice convention is now load-bearing across eight services but is still marketed in `CLAUDE.md` as an exception list that grows with every pilot. Without the ninth pilot, the eight existing exception blocks cannot collapse into a single default-shape paragraph.

## Solution

Refactor api-gateway as the ninth and final Clean Architecture + Vertical Slices pilot, then file a new ADR that promotes the convention from "per-service pilot exception" to **default service shape**, and sweep `CLAUDE.md` so the per-service exception blocks collapse into one paragraph that lists only genuine divergences (no `Domain/` or `Contracts/` for Auth/Gateway, no outbox seam for Basket/Inventory/Shipping/Saga, multi-producer slice convention for Payment, etc.).

Target layout: `Features/Operator/<EndpointSlice>/` + `Infrastructure/{Proxy,Polling,Auth,...}/`. No `Domain/` (gateway owns no aggregate — `DeadLetterMessage` lives in `ECommerce.Shared`). No `Contracts/Integration/` (gateway publishes no integration events). Boundaries enforced by both NetArchTest (`ApiGateway.Tests/Architecture/LayoutTests.cs`) and a new Roslyn `ApiGateway.Service.LayoutAnalyzer` matching the seven prior pilots.

Behaviour is identical post-refactor: same routes, same auth policies, same provider switch, same DLQ poller cadence, same observability sources, same configuration keys.

## User Stories

1. As a developer onboarding to api-gateway, I want every operator HTTP endpoint to live in its own `Features/Operator/<EndpointName>/` folder, so that I can locate any endpoint by name without grepping a static module class.
2. As a developer adding a new operator endpoint, I want a per-slice folder with one `Endpoint`, one `Handler`, one `Request`, and one `Response` file, so that I follow the same scaffolding as Order/Product/Basket/Auth/Inventory/Shipping/Payment/Saga without re-learning a separate convention.
3. As a developer changing replay behaviour, I want `ReplayFailure` slice files to be the only files I touch, so that batch-replay and single-replay edits stay surgical and reviewable.
4. As a reviewer auditing a PR, I want NetArchTest assertions that fail the build when a slice references another slice or when `Infrastructure/` references `Features/`, so that boundary violations are caught in CI rather than at code-review time.
5. As a reviewer auditing a PR, I want a Roslyn `LayoutAnalyzer` that surfaces boundary violations at compile-time inside the IDE, so that the violation is visible before the test run.
6. As a developer changing the proxy provider switch (YARP/Ocelot), I want `Infrastructure/Proxy/{Yarp,Ocelot,SwaggerAggregation}/` co-located, so that proxy plumbing is grouped exactly the way Shipping groups carrier plumbing under `Infrastructure/Carriers/`.
7. As a developer changing the outbox failure poller, I want `Infrastructure/Polling/` to hold the `OutboxFailurePoller`, `OutboxFailureClient`, `OutboxFailureItem`, and `OutboxPollerOptions` together, so that polling internals are one folder instead of nested under `Operator/OutboxPolling/`.
8. As an operator hitting `GET /operator/api/failures`, I want list semantics, filters, pagination, and response shape unchanged, so that existing Bruno collections / dashboards continue to work without changes.
9. As an operator hitting `GET /operator/api/failures/{id}`, I want detail response shape (`DeadLetterDetailResponse` carrying `Message` + `TraceUrl`) unchanged, so that linked tracing UIs continue to resolve correlation IDs.
10. As an operator hitting `POST /operator/api/failures/{id}/replay`, I want the same outcome → HTTP mapping (Success → 202, NotFound → 404, NotPending → 409, PublishFailed → 502), so that existing retry tooling reads the same status codes.
11. As an operator hitting `POST /operator/api/failures/{id}/discard`, I want the same `reason`-required validation and the same outcome → HTTP mapping, so that the audit trail keeps the same shape.
12. As an operator hitting `POST /operator/api/failures/replay-batch`, I want the per-item status mapping unchanged, so that batch dashboards keep reading the same `success|not_found|not_pending|publish_failed|unknown` values.
13. As an SRE running a YARP-backed gateway, I want `Gateway:Provider=Yarp` (default) to behave identically to today, including all `ReverseProxy:Clusters:*:Destinations:default:Address` overrides driven by `Gateway:ClusterAddresses:*`, so that no deployment changes are required.
14. As an SRE running an Ocelot-backed gateway, I want `Gateway:Provider=Ocelot` to behave identically to today, so that the fallback option from ADR-0001 remains a one-config-flip rollback.
15. As an SRE, I want unknown values for `Gateway:Provider` to fail fast on startup with the existing error message, so that misconfiguration cannot silently degrade routing.
16. As an SRE, I want the aggregated Swagger UI exposed by `SwaggerAggregation` to render identically to today, so that downstream documentation consumers see no diff.
17. As an SRE, I want `/health/{live,ready}` endpoints and `/metrics` Prometheus scrape to keep working with no path or shape change, so that probes and dashboards remain valid.
18. As a security reviewer, I want `RequireOperator` policy enforcement on every `/operator/api/failures*` route unchanged, so that authorization regressions are impossible in this refactor.
19. As a security reviewer, I want the `JwtClaimTypes.Subject = "sub"` constant moved out of `OperatorModule.cs` (where it currently sits as a private internal class at the bottom of the file) and into a gateway-internal `Infrastructure/Auth/JwtClaimTypes.cs`, so that JWT-claim string literals are not buried inside a domain module.
20. As a developer running `dotnet test` in `api-gateway/`, I want test files mirroring the production `Features/` tree (`ApiGateway.Tests/Features/Operator/<EndpointName>/`), so that the navigation symmetry between slice and slice-test is preserved.
21. As a developer running `dotnet test`, I want `OutboxFailurePollerTests` relocated to `ApiGateway.Tests/Infrastructure/Polling/`, so that the poller test sits next to where the poller lives in production code.
22. As a developer running `dotnet test`, I want existing integration tests (`Integration/GatewayIntegrationTests.cs`, `Integration/SwaggerAggregationIntegrationTests.cs`) to stay at the project root unchanged, so that boot-end integration tests remain framework-style rather than slice-style.
23. As an architect, I want a new ADR that promotes Clean Architecture + Vertical Slices from "per-service pilot exception" to **default service shape**, supersedes the exception-list sections of ADR-0011, and lists only the genuine per-service divergences, so that the convention is no longer marketed as a series of one-off exceptions.
24. As a future reader of `CLAUDE.md`, I want the per-service "service exception" paragraphs (Order, Product, Basket, Auth, Inventory, Shipping, Payment, Saga) collapsed into one default-shape paragraph plus a short list of remaining divergences, so that `CLAUDE.md` shrinks instead of growing every pilot.
25. As a developer running pre-commit hooks, I want `dotnet format --verify-no-changes`, `dotnet build`, and the Basket test smoke pass to succeed without `--no-verify`, so that the sandbox policy in `CLAUDE.md` continues to hold.
26. As a developer reviewing the diff, I want zero behavioural change in the gateway DLQ poller's upsert semantics (existing-vs-new branching on `Origin == Outbox && Service == serviceName && Id == failureId`), so that the only delta is layout.
27. As a developer reviewing the diff, I want zero behavioural change in `WebApplicationFactory<Program>`-based integration tests, including the `public partial class Program {}` at the bottom of `Program.cs`, so that test boot semantics are preserved.

## Implementation Decisions

**Target layout.**

```
api-gateway/
  ApiGateway/
    Features/
      Operator/
        ListFailures/
        GetFailureDetail/
        ReplayFailure/
        DiscardFailure/
        BatchReplayFailures/
    Infrastructure/
      Proxy/
        Yarp/
        Ocelot/
        SwaggerAggregation/
        GatewayProvider.cs
        GatewayProviderExtensions.cs
        GatewayProviderOptions.cs
      Polling/
        OutboxFailurePoller.cs
        OutboxFailureClient.cs
        OutboxFailureItem.cs
        OutboxPollerOptions.cs
      Auth/
        JwtClaimTypes.cs
    Program.cs
  ApiGateway.Tests/
    Features/Operator/<EndpointSlice>/
    Infrastructure/Polling/
    Infrastructure/Proxy/{Yarp,Ocelot,SwaggerAggregation}/
    Architecture/LayoutTests.cs
    Integration/
  ApiGateway.Service.LayoutAnalyzer/ (new Roslyn analyzer project)
```

**No `Domain/`, no `Contracts/Integration/`.** Gateway owns no aggregate (`DeadLetterMessage` and friends live in `ECommerce.Shared.Infrastructure.DeadLetter`) and publishes no integration events (it consumes failed outbox HTTP feeds and persists to a gateway-owned store). The Auth-minus-Contracts shape is followed with the further minus of `Domain/`. The new ADR records this as a permitted divergence.

**One slice per operator endpoint.** Five slices for the five existing HTTP routes (list, detail, replay, discard, batch-replay). Each slice contains the endpoint mapping, the handler (delegating to `IDeadLetterReplayer` / `IDeadLetterDiscarder` / `IDeadLetterStore` from shared), the request DTO, and the response DTO. Existing helper records (`DeadLetterDetailResponse`, `DiscardRequest`, `BatchReplayRequest`, `BatchReplayItem`, `BatchReplayResponse`) migrate into the slice that owns them.

**Per-slice DI wiring.** Each slice owns its own `AddSlice(IServiceCollection, IConfiguration)` + `MapSlice(IEndpointRouteBuilder)` extensions, called from `Program.cs`. The current `OperatorModule.AddServices` / `MapEndpoints` god-methods are deleted; their shared DI calls (`AddPlatformEventBus`, `AddDeadLetter`, `AddRequireOperatorPolicy`) move to `Program.cs` directly because they are cross-cutting, not slice-specific.

**Proxy stays Infrastructure plumbing.** `YarpGatewayModule`, `OcelotGatewayModule`, `GatewayProvider*`, and `SwaggerAggregation/*` move under `Infrastructure/Proxy/` with no behavioural change. Justification: proxy has no inbound user trigger; it is internal plumbing, exactly mirroring Shipping's `Infrastructure/Carriers/` decision and Saga's `Infrastructure/Reaper/` decision. The provider switch (`AddConfiguredGateway`, `UseConfiguredGatewayAsync`) keeps its current shape; only the namespace moves.

**Poller stays Infrastructure plumbing.** `OutboxFailurePoller` (hosted `BackgroundService`) + `OutboxFailureClient` + `OutboxFailureItem` + `OutboxPollerOptions` move under `Infrastructure/Polling/`. Same rationale as Shipping's `CarrierPollingService`: internal scheduling, no inbound trigger. Configuration shape (`Operator:OutboxPolling`, `Enabled`, `IntervalSeconds`, `Services[]`) is preserved.

**JWT helper.** `JwtClaimTypes.Subject` moves to `Infrastructure/Auth/JwtClaimTypes.cs` as `internal static class` inside the gateway project. Not promoted to `ECommerce.Shared` (separate decision, not in scope).

**Guardrails (both layers).**

- *NetArchTest* in `ApiGateway.Tests/Architecture/LayoutTests.cs`. Rules: (a) types under `Features.<X>` may not reference types under `Features.<Y>` where `X != Y`; (b) types under `Infrastructure.*` may not reference `Features.*`; (c) `Features.*` may reference `Infrastructure.*` and `ECommerce.Shared.*`; (d) top-level `Endpoints/`, `ApiModels/`, `Models/` folders do not exist; (e) `Domain/` and `Contracts/` do not exist (explicit gateway divergence assertion).
- *Roslyn analyzer* `ApiGateway.Service.LayoutAnalyzer/` cloning the shape of `Inventory.Service.LayoutAnalyzer` / `Shipping.Service.LayoutAnalyzer`. Emits compile-time diagnostics for the same rules.

**Routes, auth, configuration, observability.** Identical to today. `/operator/api/failures*`, `/health/*`, `/metrics`, all proxy routes, `Gateway:Provider`, `Gateway:ClusterAddresses:*`, `Operator:OutboxPolling:*`, `Operator:TraceUiBaseUrl`, `AuthorizationPolicies.RequireOperatorPolicy`, `AddJwtAuthentication`, `AddPlatformObservability`, `AddPlatformHealthChecks`, `app.ApplyDeadLetterMigrations()` in Dev — all unchanged in behaviour, only namespace/folder shifts.

**Companion ADR + CLAUDE.md sweep.** A new ADR (`docs/adr/00XX-clean-arch-vsa-default-service-shape.md`) records the promotion. It supersedes the per-service exception sections of ADR-0011 and lists permitted divergences: (a) Auth has no `Contracts/`; (b) Gateway has neither `Contracts/` nor `Domain/`; (c) Basket/Inventory/Shipping/Saga have no `IIntegrationMap<,>`/outbox interceptor seam; (d) Payment's multi-producer slice convention (HTTP + saga slices share a single mapper via DI); (e) Saga's two-level `Features/<Saga>/<Trigger>/` nesting and dual-subscription convention. The `CLAUDE.md` "Cross-service architecture" section is rewritten: the eight per-service exception paragraphs collapse into one default-shape paragraph plus the divergence list above.

**Shared library.** No changes. Gateway already consumes `ECommerce.Shared` 2.23.0/2.25.0 via the local NuGet feed; this refactor does not require a shared-lib version bump.

**Program.cs.** Stays at project root. Calls per-slice `AddSlice`/`MapSlice` extensions plus the cross-cutting `AddJwtAuthentication`, `AddPlatformObservability`, `AddPlatformHealthChecks`, `AddConfiguredGateway`/`UseConfiguredGatewayAsync`, `app.ApplyDeadLetterMigrations()`. The `public partial class Program {}` declaration at the bottom of the file is preserved so `WebApplicationFactory<Program>` continues to work.

## Testing Decisions

**What makes a good test here.** Test external behaviour only: HTTP status codes, response bodies, header values, persisted DLQ rows, log events emitted, outcome mappings. Do not assert internal helper-class names, slice file paths, or DI-container internals. The Roslyn analyzer + NetArchTest assertions are the layout tests — unit tests do not duplicate them.

**Test-side reorganisation.**

- `ApiGateway.Tests/Features/Operator/ListFailures/` — replaces operator list endpoint tests
- `ApiGateway.Tests/Features/Operator/GetFailureDetail/DeadLetterDetailEndpointTests.cs` — relocated, unchanged behaviour
- `ApiGateway.Tests/Features/Operator/ReplayFailure/` — replay outcome-to-HTTP mapping
- `ApiGateway.Tests/Features/Operator/DiscardFailure/DeadLetterDiscardEndpointTests.cs` — relocated
- `ApiGateway.Tests/Features/Operator/BatchReplayFailures/DeadLetterReplayBatchEndpointTests.cs` — relocated
- `ApiGateway.Tests/Operator/OperatorEndpointsRoutingTests.cs` + `OperatorMessagingProviderTests.cs` — reclassified as cross-slice routing / DI boot tests; relocated to `ApiGateway.Tests/Composition/` (one folder, not per-slice) because they test boot-time wiring, not slice behaviour.
- `ApiGateway.Tests/Infrastructure/Polling/OutboxFailurePollerTests.cs` — relocated
- `ApiGateway.Tests/Infrastructure/Proxy/{Yarp,Ocelot,SwaggerAggregation}/` — holds the relocated `GatewayProviderExtensionsTests`, `Gateway/SwaggerAggregation/*` tests
- `ApiGateway.Tests/Integration/` — untouched; `GatewayIntegrationTests`, `GatewayWebApplicationFactory`, `StubHttpServer`, `SwaggerAggregationIntegrationTests`, `SwaggerStubServer` stay
- `ApiGateway.Tests/Architecture/LayoutTests.cs` — new file; NetArchTest assertions per the rules above

**Modules to test (new or relocated).**

- Each `Features/Operator/<Slice>/` handler — already covered by the existing endpoint tests; relocation only
- `ApiGateway.Service.LayoutAnalyzer` — new analyzer project; clone the test pattern from `Inventory.Service.LayoutAnalyzer.Tests` / `Shipping.Service.LayoutAnalyzer.Tests` if those exist, otherwise a minimal "violation diagnoses, conformant code does not" test class
- `Architecture/LayoutTests.cs` — itself the test; no further tests-of-tests

**Prior art.**

- `Shipping.Tests/Architecture/LayoutTests.cs` for NetArchTest rule shape
- `Inventory.Tests/Architecture/LayoutTests.cs` for the no-`Endpoints/ApiModels/Models` assertion
- `Auth.Tests/Architecture/LayoutTests.cs` for the no-`Contracts/` assertion (gateway extends this to also assert no `Domain/`)
- `Saga.Tests/Architecture/LayoutTests.cs` for cross-slice no-reference rules
- `Inventory.Service.LayoutAnalyzer` / `Shipping.Service.LayoutAnalyzer` for the Roslyn analyzer scaffolding

## Out of Scope

- Promoting `JwtClaimTypes` to `ECommerce.Shared.Authentication` (deferred; gateway-internal only here).
- DLQ replay batching enhancements (separate AFK issue, not part of layout refactor).
- Provider-agnostic DLQ capture/replay (still uses RabbitMQ-specific `DeadLetterHostedService` and `RabbitMqDeadLetterPublisher`; messaging PRD C territory).
- Refactoring `ECommerce.Shared.Infrastructure.DeadLetter` shape — the store/replayer/discarder contracts and the `DeadLetterMessage` entity stay as-is.
- Removing any divergence flagged by the new ADR (Payment multi-producer, Saga two-level nesting, missing-outbox-seam services, etc.) — the new ADR *records* divergences, it does not erase them.
- Shared-lib version bump or `.nupkg` re-pack — not required for this refactor.
- GitHub Actions / CI pipeline changes — Azure Pipelines per-service definitions continue to work unchanged.

## Further Notes

- Gateway is the **ninth** pilot; the saga exception block in `CLAUDE.md` claims saga was "the eighth and final" — that claim is corrected as part of this PRD's `CLAUDE.md` sweep. The new wording: "every service in the monorepo is on Clean Architecture + Vertical Slices; api-gateway closed out the migration."
- The new ADR explicitly supersedes the "Propagation to remaining services is a separate ADR" footer present at the end of seven prior pilot exception blocks. Those footers were forward-looking; they are now historical.
- Pre-commit hook policy (Husky.Net, `dotnet format --verify-no-changes` → `dotnet build` → Basket tests) applies unchanged. Cross-service test runs (Order, Product, Auth, Inventory, Shipping, Payment, Saga, ApiGateway) must be done manually before pushing per the existing `CLAUDE.md` rule.
- Phase ordering suggestion (for the follow-up implementation plan, not this PRD): (1) create analyzer + LayoutTests asserting the *target* layout to lock the goalposts; (2) move proxy + poller into `Infrastructure/`; (3) carve operator endpoints into per-slice folders one at a time, relocating tests in the same commit; (4) delete `OperatorModule.cs` once empty; (5) file the new ADR + `CLAUDE.md` sweep as the closing commit.
