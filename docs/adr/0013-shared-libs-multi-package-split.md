# ADR-0013 — `ECommerce.Shared` split into eight capability packages plus an umbrella metapackage

- **Status**: Accepted
- **Date**: 2026-05-25

## Context

`shared-libs/ECommerce.Shared` is the one platform library every microservice and the api-gateway depends on. ADR-0012 promoted Clean Architecture + Vertical Slices to the default *service* shape, but the platform library that everything depends on was still a single assembly with a flat per-capability folder layout (`Authentication/`, `HealthChecks/`, `Observability/`, `OpenApi/`, `Qa/`, `IntegrationEvents/Commands/`, `Infrastructure/{EventBus,Messaging,Outbox,RabbitMq,AzureServiceBus,DeadLetter}/`) and no enforced internal boundaries. Three pains followed:

1. **Version skew across consumers.** Auth, Basket, Product pin `2.18.0`; Order pins `2.24.0`; Inventory, Payment, Shipping, Saga, ApiGateway pin `2.25.0`. The skew was documented at `shared-libs/CLAUDE.md` §"Version pinning history" and forced shared-libs to maintain backwards-compatible behaviour across three minor versions in production at once.
2. **Latent eager-broker defect carried by older pins.** The lazy `IRabbitMqConnection` registration fix shipped in `2.24.0` (commit `dcbc29c`). Auth, Basket, Product still pin pre-fix versions and silently boot with the eager defect — the only reason it does not break their integration tests is that those services do not yet have a `MessagingProviderBootTests`-equivalent gate.
3. **Boundary rot hidden by `InternalsVisibleTo`.** `Authentication`, `Observability`, `Outbox`, `RabbitMq`, and `DeadLetter` all live in the same assembly. `OpenTelemetryStartupExtensions` reached into `internal static` constants on `OutboxTelemetry`, `DeadLetterMetrics`, and `RabbitMqTelemetry` because `InternalsVisibleTo` made the violation invisible. Nothing in the build prevented a new contributor from making `Authentication` depend on `RabbitMq` or `Contracts` depend on `Outbox`.

PRD: [PRD-Shared-Libs-CleanArch-VSA-Split.md](../prd/PRD-Shared-Libs-CleanArch-VSA-Split.md). Plan: [shared-libs-cleanarch-vsa-split.md](../plans/shared-libs-cleanarch-vsa-split.md). Source lives under [`shared-libs/`](../../shared-libs/); consumer-side runbook for bump-and-publish is [`shared-libs-versioning.md`](../runbooks/shared-libs-versioning.md).

## Decision

Restructure `shared-libs/ECommerce.Shared` into **eight capability NuGet packages** plus one **umbrella metapackage** that preserves the existing package name, namespace shape, and consumer csproj reference. The eight packages each carry an inner Clean Architecture triad (`Abstractions/` + `Impl/` + `Composition/`), enforced by a single Roslyn `ECommerce.Shared.LayoutAnalyzer` project plus per-package NetArchTest `LayoutTests.cs`. All packages share one `<Version>` via a centralised [`shared-libs/Directory.Build.props`](../../shared-libs/Directory.Build.props) and ship together as v3.0.0.

### Package partition (8 + 1 umbrella)

Nine csprojs in [`shared-libs/`](../../shared-libs/), one solution at [`shared-libs/ECommerce.Shared.slnx`](../../shared-libs/ECommerce.Shared.slnx):

- [`ECommerce.Shared.Kernel`](../../shared-libs/ECommerce.Shared.Kernel/) — primitives only: `Event` base class, telemetry name constants under `Kernel/Abstractions/TelemetryConventions/`, `MessagingOptions`, `MetricFactory`. Depends on nothing.
- [`ECommerce.Shared.EventBus`](../../shared-libs/ECommerce.Shared.EventBus/) — `IEventBus`, `IEventHandler`, `EventBusOptions`, plus the entire `Outbox/` capability (context, background service, unit-of-work, migrations, telemetry). Depends on Kernel.
- [`ECommerce.Shared.RabbitMq`](../../shared-libs/ECommerce.Shared.RabbitMq/) — `IRabbitMqConnection`, `RabbitMqConnection`, `RabbitMqEventBus`, hosted service, options, topology. Depends on Kernel + EventBus.
- [`ECommerce.Shared.AzureServiceBus`](../../shared-libs/ECommerce.Shared.AzureServiceBus/) — full ASB stack mirroring RabbitMq shape. Depends on Kernel + EventBus.
- [`ECommerce.Shared.DeadLetter`](../../shared-libs/ECommerce.Shared.DeadLetter/) — `DeadLetterDbContext`, capture/publisher/replayer/discarder interfaces and broker-specific impls, plus the `MessagingProviderResolver` (co-located here because DLQ is the only consumer of the resolver outside the broker packages). Depends on Kernel + RabbitMq + AzureServiceBus.
- [`ECommerce.Shared.Platform`](../../shared-libs/ECommerce.Shared.Platform/) — Authentication + Observability + HealthChecks + OpenApi bundled. Observability reads telemetry names from Kernel. Depends on Kernel.
- [`ECommerce.Shared.Contracts`](../../shared-libs/ECommerce.Shared.Contracts/) — the eleven saga command POCOs from the old `IntegrationEvents/Commands/`. Depends on Kernel.
- [`ECommerce.Shared.Testing.Qa`](../../shared-libs/ECommerce.Shared.Testing.Qa/) — `QaPersonas`, `QaSeedingExtensions`. Depends on Kernel.
- [`ECommerce.Shared`](../../shared-libs/ECommerce.Shared/) (umbrella) — zero `.cs` files. Eight `<ProjectReference>` lines and nothing else. Pack converts those into eight `<PackageReference>` lines at v3.0.0.

### Umbrella metapackage strategy

Every consumer csproj continues to reference `ECommerce.Shared` by name. The umbrella's only purpose is to depend on the eight sub-packages so that one reference brings the whole platform. No consumer is required to migrate to narrow packages — narrow re-pinning is a future opportunistic PR per consumer, not a precondition of this work.

### Stable namespaces

Namespaces inside each new package stay byte-identical to today's locations (e.g. `ECommerce.Shared.Infrastructure.EventBus` lives inside `ECommerce.Shared.EventBus.csproj` but the C# `namespace` declaration is unchanged). C# has no cross-assembly namespace aliasing, so renaming would break every consumer `using` statement; the split preserves namespaces so every consumer's source files compile without edits.

### Inner shape per package: `Abstractions/` + `Impl/` + `Composition/`

- `Abstractions/` holds public interfaces, POCOs, and options classes (the "port").
- `Impl/` holds concrete classes, EF configurations, telemetry helpers, broker-specific adapters (the "adapter").
- `Composition/` holds the `AddX()` DI extension methods (the "wire").

Boundaries are enforced by namespace + file-path conventions checked at compile time by the analyzer; csproj boundaries are at package granularity, not at layer granularity.

### Telemetry conventions in Kernel

`OutboxTelemetry.ActivitySourceName`, `OutboxTelemetry.MeterName`, `DeadLetterMetrics.ActivitySourceName`, `DeadLetterMetrics.MeterName`, `RabbitMqTelemetry.ActivitySourceName`, and the Azure Service Bus equivalents are promoted from `internal static` to `public const` strings and moved into `ECommerce.Shared.Kernel/Abstractions/TelemetryConventions/`. The constant values are preserved byte-for-byte (`"ECommerce.Shared.Outbox"`, `"ECommerce.Shared.DeadLetter"`, `"RabbitMQEventBus"`, `"AzureServiceBusEventBus"`), so Grafana panels, Prometheus alert rules, and Jaeger queries continue to match without dashboard edits. `Platform.Observability.OpenTelemetryStartupExtensions` reads from Kernel, resolving the cross-package internal-visibility violation that today was hidden by `InternalsVisibleTo`.

### Boundary enforcement — belt and suspenders

Two redundant guardrails run on every commit; the redundancy is intentional, because they fire at different times.

- **Roslyn analyzer** ([`ECommerce.Shared.LayoutAnalyzer`](../../shared-libs/ECommerce.Shared.LayoutAnalyzer/)) — three rules raised as build-time compiler errors:
  1. `SHALAY001` — files under `<Package>/Abstractions/` may not `using` any namespace owned by `<Package>/Impl/`.
  2. `SHALAY002` — files under `<Package>/Impl/` may not `using` any namespace owned by `<Package>/Composition/`.
  3. `SHALAY003` — files in package `<X>` may not `using ECommerce.Shared.*` outside the per-package allowlist that encodes the documented dependency arrows.
- **NetArchTest** — each per-package test csproj under [`shared-libs/tests/`](../../shared-libs/tests/) ships `Architecture/LayoutTests.cs` asserting the same three rules via fluent assertions, mirroring [`order-microservice/Order.Tests/Architecture/LayoutTests.cs`](../../order-microservice/Order.Tests/Architecture/LayoutTests.cs).

The analyzer fires during `dotnet build`, including in IDE quick-feedback. NetArchTest fires during `dotnet test`. AI-assisted edits surface violations at the earliest possible moment, and CI catches them again as a defence in depth. Diagnostic prefix matches the repo's `<svc>LAY###` convention (`ORDLAY`/`AGWLAY`).

### Lockstep versioning

`shared-libs/Directory.Build.props` defines `<Version>3.0.0</Version>` for the whole shared-libs solution. All nine csprojs inherit. Every release bumps one place; no sibling-version skew is possible. The decision is a deliberate departure from independent semver for sibling packages — the rationale (eliminate version-skew bugs like the prior 2.18 / 2.24 / 2.25 split) outweighs the small loss of per-package release granularity.

### Pack and publish flow

`dotnet pack -c Release shared-libs/ECommerce.Shared.slnx` packs the whole solution and emits nine `*.3.0.0.nupkg` files; a glob copy moves them into `local-nuget-packages/`. Runbook: [`docs/runbooks/shared-libs-versioning.md`](../runbooks/shared-libs-versioning.md). The runbook also documents the consumer version-pin sweep (one PR per consumer, low-risk-first order).

### Test redistribution

The flat `ECommerce.Shared.Tests/` csproj is dissolved into eight per-package test csprojs under [`shared-libs/tests/`](../../shared-libs/tests/), each with a single `<ProjectReference>` to its own production package (plus a small number of documented cross-boundary refs for tests that legitimately span packages, e.g. DLQ + broker integration tests). Each src package csproj `<InternalsVisibleTo>`s only its own `.Tests` peer — sibling-package internals are no longer reachable from tests. An additional [`ECommerce.Shared.LayoutAnalyzer.Tests`](../../shared-libs/tests/ECommerce.Shared.LayoutAnalyzer.Tests/) csproj uses `Microsoft.CodeAnalysis` to compile synthetic violation snippets and assert SHALAY001/SHALAY003 fire with the expected diagnostic IDs.

### Behaviour-preserving public surface

All `Add*` DI extension method signatures are unchanged. The `Messaging:Provider=RabbitMQ|AzureServiceBus` switch is unchanged. JWKS cache cadence and `RequireService`/`RequireOperator` claim requirements are unchanged. `OutboxContext` and `DeadLetterDbContext` schemas are byte-identical post-split — no migrations against any consumer database. The only observable change for consumers is a one-line version bump `2.x.0` → `3.0.0`, which sweeps the documented eager-broker defect across the four older pins as a side effect.

## Consequences

- One folder per capability: opening `shared-libs/ECommerce.Shared.<Package>/` shows everything that package owns, including its DI wiring. Adding a new capability is a new csproj + one `<ProjectReference>` line in the umbrella.
- Two guardrails (Roslyn analyzer + NetArchTest) catch boundary violations at build time and at test time. AI-assisted edits cannot silently cross package boundaries; new contributors cannot make `Authentication` depend on `RabbitMq` without the build failing.
- Cost: nine csprojs to maintain instead of one. Justified by the version-skew elimination, the boundary enforcement, and the optionality of future narrow-package consumer pinning.
- Cost: lockstep versioning forgoes per-package release granularity. The team accepted this in exchange for eliminating the 2.18 / 2.24 / 2.25 skew. If per-package release cadence becomes necessary later, the lockstep rule is recorded here and can be revisited via a follow-up ADR.
- Cost: adding a ninth capability package requires an entry in the analyzer's package allowlist plus an `<InternalsVisibleTo>` line in the new csproj. Documented in [`shared-libs-versioning.md`](../runbooks/shared-libs-versioning.md).
- The 9-PR consumer sweep (auth → basket → product → order → inventory → payment → shipping → saga → api-gateway) ships post-ADR and closes the latent eager-broker defect by upgrading every consumer past `2.24.0`. After the sweep, every consumer pins `3.0.0` and `shared-libs/CLAUDE.md` §"Version pinning history" records the convergence.

## Supersedes / Composes

- **Composes [ADR-0005](0005-ecommerce-shared-as-nuget-via-local-feed.md) by reference.** The local-feed distribution decision is unchanged; this ADR only changes the *count* of `.nupkg` files produced (nine instead of one) and the inner shape of the source tree. The local feed at [`local-nuget-packages/`](../../local-nuget-packages/) remains the publish target.
- **Composes [ADR-0011](0011-order-cleanarch-vsa-pilot.md) and [ADR-0012](0012-clean-arch-vsa-default-service-shape.md) by analogy.** The same `Abstractions/` + `Impl/` + `Composition/` triad that ADR-0011 introduced for `Order.Service` (and ADR-0012 promoted to the default service shape) is the inner shape here — parametrised over package names instead of slice names. The two-layer enforcement (NetArchTest + Roslyn analyzer) is the same pattern, ported to the shared-libs solution.
- Does not supersede any prior ADR. ADR-0005's local-feed distribution model and ADR-0012's per-service default shape both remain in force.
