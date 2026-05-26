# PRD: `ECommerce.Shared` Clean Architecture + Vertical Slices split

## Problem Statement

`shared-libs/ECommerce.Shared` is the platform library every service and the api-gateway depends on. It ships as a single NuGet package with a flat per-capability folder layout (`Authentication/`, `HealthChecks/`, `Observability/`, `OpenApi/`, `Qa/`, `IntegrationEvents/Commands/`, `Infrastructure/{EventBus,Messaging,Outbox,RabbitMq,AzureServiceBus,DeadLetter}/`). The default-shape sweep (ADR-0012) promoted Clean Architecture + Vertical Slices to the convention for every *service* in the monorepo, yet the **platform library that everything depends on** is still organised as one assembly with no enforced internal boundaries.

The pain shows up three ways:

- **Version skew across consumers.** Auth, Basket, Product pin `2.18.0`; Order pins `2.24.0`; Inventory, Payment, Shipping, Saga, ApiGateway pin `2.25.0`. The skew is documented at `shared-libs/CLAUDE.md` §"Version pinning history" and forces shared-libs to maintain backward-compatible behaviour across three minor versions in production at once.
- **Latent eager-broker defect carried by older pins.** The lazy `IRabbitMqConnection` registration fix shipped in `2.25.0` (commit `dcbc29c`). Auth, Basket, Product, and Order still pin pre-fix versions and silently boot with the eager defect — the only reason it does not break their integration tests is that those services do not yet have a `MessagingProviderBootTests`-equivalent gate.
- **Boundary rot.** Inside the single library, `Authentication`, `Observability`, `Outbox`, `RabbitMq`, and `DeadLetter` all live in the same assembly. `OpenTelemetryStartupExtensions` reaches into `internal static` constants on `OutboxTelemetry`, `DeadLetterMetrics`, and `RabbitMqTelemetry` because `InternalsVisibleTo` makes the violation invisible. Nothing in the build prevents a new contributor from making `Authentication` depend on `RabbitMq` or `Contracts` depend on `Outbox`.

From a maintainer's perspective the result is: a shared library that *talks* about Clean Architecture in its commit history but does not *enforce* it, plus a recurring "which version is each service on this week?" question with no clean answer.

## Solution

Restructure `shared-libs/ECommerce.Shared` into **eight capability NuGet packages** plus one **umbrella metapackage** that preserves the existing package name, namespace shape, and consumer csproj reference. The eight packages each get an inner Clean Architecture triad (`Abstractions/` + `Impl/` + `Composition/`), enforced by a single Roslyn `ECommerce.Shared.LayoutAnalyzer` project plus per-package NetArchTest `LayoutTests.cs`. All packages share one `<Version>` via a centralized `shared-libs/Directory.Build.props` (lockstep semver) and ship together as v3.0.0.

The umbrella `ECommerce.Shared` package remains on every consumer's csproj as a single `<PackageReference>`. It contains no compiled source — only eight transitive `<PackageReference>` lines. Consumer code is **untouched**: namespaces inside the eight new packages stay byte-identical to today's (`ECommerce.Shared.Infrastructure.EventBus`, `ECommerce.Shared.Authentication`, etc.), so every `using` statement in every consumer continues to compile.

Behaviour is identical post-refactor: same DI extension method signatures, same options classes, same RabbitMQ/Azure Service Bus topology, same outbox schema, same DLQ store schema, same telemetry source/meter names, same JWT validation pipeline, same health check probes. The only observable change for consumers is a one-line version bump `2.x.0` → `3.0.0`, which sweeps the documented eager-broker defect across the four older pins as a side effect.

A follow-up 9-PR sweep (one per consumer service + api-gateway) bumps the umbrella version pin from each service's current value to `3.0.0`. Consumers may optionally re-pin to narrow packages opportunistically in future PRs, but no consumer is required to.

## User Stories

1. As a contributor adding a new shared capability, I want the library to be split into capability NuGet packages, so that I can locate the existing capability folder by package name without grepping a flat `Infrastructure/` tree.
2. As a contributor extending the event bus, I want `ECommerce.Shared.EventBus` to contain both the `IEventBus` abstraction and the entire `Outbox/` capability bundled together, so that the publish-via-outbox seam stays atomic and discoverable in one package.
3. As a contributor extending RabbitMQ support, I want `ECommerce.Shared.RabbitMq` to be a separate package, so that consumers who only need the Azure Service Bus provider can in future opt out of the RabbitMQ dependency.
4. As a contributor extending Azure Service Bus support, I want `ECommerce.Shared.AzureServiceBus` to be a separate package, so that RabbitMQ-only consumers can in future opt out of the ASB transitive dependency.
5. As a contributor working on dead-letter capture and replay, I want `ECommerce.Shared.DeadLetter` to depend explicitly on both broker packages, so that the broker-specific capture seams (`RabbitMqDeadLetterCapture`, `AzureServiceBusDeadLetterCapture`) compose without crossing back into the broker packages.
6. As a contributor working on cross-cutting platform concerns, I want `ECommerce.Shared.Platform` to bundle Authentication + Observability + HealthChecks + OpenApi, so that one DI extension call (`AddPlatformObservability` etc.) wires the lot from a single package without dragging in messaging or outbox.
7. As a contributor adding a saga command, I want `ECommerce.Shared.Contracts` to contain only POCOs that depend on `ECommerce.Shared.Kernel`, so that producer and consumer services share a contracts-only dependency with no infrastructure transitive surface.
8. As a contributor writing test fixtures, I want `ECommerce.Shared.Testing.Qa` to be a separate package, so that production deployments can in future exclude QA seeding code from the runtime image.
9. As a contributor reading file paths, I want each of the eight packages to organise its files into `Abstractions/`, `Impl/`, and `Composition/`, so that the public contract (interfaces, options, POCOs), the concrete implementations, and the DI wiring are visible at a glance from the folder tree.
10. As a contributor accidentally adding `using <pkg>.Impl;` inside an `Abstractions/` file, I want the build to fail with a Roslyn diagnostic SHA0001, so that the Clean Architecture boundary is caught at compile time inside the IDE, not at code-review time.
11. As a contributor accidentally adding `using <pkg>.Composition;` inside an `Impl/` file, I want the build to fail with a Roslyn diagnostic SHA0002, so that DI extension wiring stays in the `Composition/` seam and does not leak into implementation code.
12. As a CI engineer, I want `LayoutTests.cs` NetArchTest assertions in every per-package test project to mirror the Roslyn rules, so that the architectural boundary is enforced twice — once at compile time in the IDE, once in CI through a test run.
13. As a consumer service maintainer (Auth, Basket, Product, Order, Inventory, Payment, Shipping, Saga, ApiGateway), I want my existing `<PackageReference Include="ECommerce.Shared" />` line to keep working with no code changes after the split, so that the upgrade to v3.0.0 is a one-line csproj bump and a full `dotnet test` pass.
14. As a consumer service maintainer, I want every `using ECommerce.Shared.Infrastructure.EventBus;` and `using ECommerce.Shared.Authentication;` in my service to continue to compile after upgrading to v3.0.0, so that no source files in any service need to be touched as part of the split.
15. As an operator running the platform, I want `dlq_messages_total`, `dlq_replays_total`, `dlq_discards_total`, `outbox_publish_failed_total`, and every `ActivitySource` used by today's tracing pipeline to keep its existing name after the split, so that Grafana panels, Prometheus alert rules, and Jaeger queries continue to match without dashboard edits.
16. As an operator running the platform, I want the JWT validation pipeline behind `AddJwtAuthentication()` to keep fetching and caching the JWKS endpoint at the same cadence and using the same key set, so that no token issued by `auth-microservice` is rejected after the upgrade.
17. As an operator running the platform, I want `RequireService` and `RequireOperator` policies to keep their current claim requirements, so that every `/internal/*` and `/operator/api/failures*` route continues to reject the same tokens as today.
18. As an operator running the platform, I want the `OutboxBackgroundService` poll interval, batch size, and retry behaviour to be identical after the split, so that no per-service outbox latency regression appears in production.
19. As an operator running the platform, I want the `DeadLetterDbContext` schema (`dead_letter_messages` table) to be byte-identical after the split, so that no migration is required against the api-gateway's database.
20. As an operator running the platform, I want the `OutboxContext` schema in every producer service to be byte-identical after the split, so that no migration is required against any producer service's database.
21. As an SRE running mixed brokers, I want `Messaging:Provider=RabbitMQ` (default) and `Messaging:Provider=AzureServiceBus` to keep selecting the same provider stack via `MessagingProviderResolver`, so that the provider flag remains the only switch for transport choice.
22. As an SRE on-call, I want the lazy `IRabbitMqConnection` registration fix (shipped in 2.25.0) to be present in every service after the version-pin sweep, so that the documented latent eager-broker defect in Auth, Basket, Product, and Order pins is closed as a side effect of the upgrade.
23. As an architect, I want a single `ECommerce.Shared.LayoutAnalyzer` Roslyn project covering all eight packages (rules parametrised by package name), so that adding a ninth package later requires only an entry in the analyzer's package allowlist, not a new analyzer project.
24. As an architect, I want all eight packages plus the umbrella to share one `<Version>` defined in `shared-libs/Directory.Build.props`, so that every release is lockstep and no consumer can ever pin a mismatched sibling-package combination.
25. As a maintainer publishing the library, I want `dotnet pack -c Release` on the solution to produce nine `.nupkg` files (`ECommerce.Shared.3.0.0.nupkg` plus eight `ECommerce.Shared.<Package>.3.0.0.nupkg` files), so that one pack command produces the full publish set for the local feed.
26. As a maintainer publishing the library, I want `shared-libs/CLAUDE.md` to document the updated nine-package pack + push recipe, so that the local NuGet feed publishing flow is correct on first read.
27. As a maintainer writing tests, I want each of the eight packages to have its own dedicated test csproj (`ECommerce.Shared.<Package>.Tests`), so that no test can accidentally exercise a sibling package's `internal` types via blanket `InternalsVisibleTo`.
28. As a maintainer writing tests, I want every existing test in the current flat `ECommerce.Shared.Tests/` project to be redistributed into the per-package test project that matches its production code, so that the new tests-per-package convention starts with the existing 20+ test files already in the right place.
29. As a developer running pre-commit hooks, I want `dotnet format --verify-no-changes`, `dotnet build`, and the Basket smoke-test pass to succeed without `--no-verify` after the split, so that the sandbox policy in `CLAUDE.md` continues to hold without exception.
30. As an architect, I want a new ADR (`docs/adr/0013-shared-libs-multi-package-split.md`) capturing the eight-package partition, the umbrella metapackage decision, the lockstep versioning decision, and the Abstractions/Impl/Composition convention, so that the decision history is durable and discoverable.
31. As an architect, I want a new runbook (`docs/runbooks/shared-libs-versioning.md`) capturing the bump-and-publish workflow, the lockstep semver rule, and the consumer version-pin sweep procedure, so that the next maintainer can ship a v3.1.0 without reverse-engineering the convention.
32. As a future reader of `CLAUDE.md`, I want the root `## Repo shape` table and the `Shared libraries` pointer to reference the new umbrella + per-package convention, so that `CLAUDE.md` reflects post-split reality.
33. As a maintainer reviewing the diff, I want zero behavioural change in any `Add*` DI extension method's public signature, so that the only delta in consumer code paths after upgrading is internal namespace organisation hidden behind the umbrella package.
34. As a maintainer reviewing the diff, I want telemetry name constants (`ActivitySourceName`, `MeterName`) extracted into `ECommerce.Shared.Kernel/Abstractions/TelemetryConventions/`, so that `Platform/Observability` can register them via Kernel without violating the locked `Platform → Kernel` dependency arrow.
35. As a maintainer running the upgrade sweep, I want a 9-PR series (one per consumer) that bumps each `<PackageReference Include="ECommerce.Shared" Version="..." />` to `3.0.0`, ordered low-risk first (auth → basket → product → order → inventory → payment → shipping → saga → api-gateway), so that any v3.0.0 ABI regression surfaces on the least-coupled consumer first.

## Implementation Decisions

**Package partition (8 capability packages + 1 umbrella, 9 csprojs total).**

- `ECommerce.Shared.Kernel` — primitives only: `Event` base class, telemetry name constants (`OutboxTelemetryNames`, `DeadLetterTelemetryNames`, `RabbitMqTelemetryNames`, `AzureServiceBusTelemetryNames`), shared `MessagingOptions`, `MetricFactory`. Depends on nothing.
- `ECommerce.Shared.EventBus` — `IEventBus`, `IEventHandler`, `EventBusOptions`, `Event` base wiring, **bundled with the entire Outbox capability** (context, background service, unit-of-work, migrations, telemetry). Depends on Kernel.
- `ECommerce.Shared.RabbitMq` — `IRabbitMqConnection`, `RabbitMqConnection`, `RabbitMqEventBus`, `RabbitMqHostedService`, `RabbitMqOptions`, `RabbitMqTopology`. Depends on Kernel + EventBus.
- `ECommerce.Shared.AzureServiceBus` — full ASB stack mirroring RabbitMQ shape. Depends on Kernel + EventBus.
- `ECommerce.Shared.DeadLetter` — `DeadLetterDbContext`, capture/publisher/replayer/discarder interfaces and broker-specific implementations, plus the `Messaging` provider resolver (resolver is co-located here because DLQ is the only consumer of the resolver outside the broker packages). Depends on Kernel + RabbitMq + AzureServiceBus.
- `ECommerce.Shared.Platform` — Authentication + Observability + HealthChecks + OpenApi bundled. Observability registers telemetry sources via Kernel constants. Depends on Kernel.
- `ECommerce.Shared.Contracts` — all eleven saga commands currently in `IntegrationEvents/Commands/`. Depends on Kernel.
- `ECommerce.Shared.Testing.Qa` — `QaPersonas`, `QaSeedingExtensions`. Depends on Kernel.
- `ECommerce.Shared` (umbrella metapackage) — empty csproj. Contains eight `<PackageReference>` lines and nothing else. Version `3.0.0`.

**Umbrella metapackage strategy.** Every consumer csproj continues to reference `ECommerce.Shared` by name. The umbrella's only purpose is to depend on the eight packages so that one reference brings the whole platform. No consumer is required to migrate to narrow packages.

**Stable namespaces.** Namespaces inside each new package stay byte-identical to today's locations (e.g. `ECommerce.Shared.Infrastructure.EventBus` lives inside `ECommerce.Shared.EventBus.csproj` but the C# namespace declaration is unchanged). C# has no cross-assembly namespace aliasing, so renaming would break every consumer `using` statement; the split preserves namespaces to keep consumer source files untouched.

**Inner shape per package: `Abstractions/` + `Impl/` + `Composition/`.** `Abstractions/` holds public interfaces, POCOs, and options classes (the "port"). `Impl/` holds concrete classes, EF configurations, telemetry helpers, and broker-specific adapters (the "adapter"). `Composition/` holds the `AddX()` DI extension methods (the "wire").

**Boundary enforcement (two layers).** A single Roslyn project `ECommerce.Shared.LayoutAnalyzer` defines three rules: SHA0001 (`Abstractions/` must not import `Impl/`), SHA0002 (`Impl/` must not import `Composition/`), SHA0003 (cross-package imports must match an allowlist matching the documented dependency arrows). Each package csproj references the analyzer. Each per-package test project has an `Architecture/LayoutTests.cs` using NetArchTest fluent assertions that mirror the Roslyn rules.

**Telemetry conventions in Kernel.** `OutboxTelemetry.ActivitySourceName`, `OutboxTelemetry.MeterName`, `DeadLetterMetrics.ActivitySourceName`, `DeadLetterMetrics.MeterName`, `RabbitMqTelemetry.ActivitySourceName`, and equivalents for Azure Service Bus are promoted from `internal static` to `public const` strings and moved into `ECommerce.Shared.Kernel/Abstractions/TelemetryConventions/`. The originals retain their constant *values* but reference Kernel for the source of truth. `Platform.Observability.OpenTelemetryStartupExtensions` reads from Kernel, resolving the cross-package internal-visibility violation that today is hidden by `InternalsVisibleTo`.

**Lockstep versioning.** A new `shared-libs/Directory.Build.props` defines `<Version>3.0.0</Version>` for the whole shared-libs solution. All nine csprojs inherit. Every release bumps one place; no sibling-version skew is possible.

**Test redistribution (8 per-package test csprojs).** The current flat `ECommerce.Shared.Tests/` project is dissolved into eight per-package test csprojs under `shared-libs/tests/`. Each tests only its own package via a single `<ProjectReference>`. Every existing test file moves to the package whose code it exercises. Each test csproj gains `Architecture/LayoutTests.cs`.

**Solution shape.** Single `shared-libs/ECommerce.Shared.slnx` listing all nine src csprojs + eight test csprojs + one analyzer csproj.

**Pack and publish flow update.** `shared-libs/CLAUDE.md` §"Pack + publish flow" is rewritten to `dotnet pack -c Release ECommerce.Shared.slnx` (whole-solution pack) followed by a glob push of all `**/bin/Release/*.3.0.0.nupkg` to `local-nuget-packages/`.

**Consumer version-pin sweep (9 PRs).** Sequence: auth → basket → product → order → inventory → payment → shipping → saga → api-gateway. Each PR is a one-line `<PackageReference>` bump plus `dotnet restore && dotnet build && dotnet test` and a docker-compose smoke test for broker-dependent services. After all nine ship, `shared-libs/CLAUDE.md` §"Version pinning history" is updated to record convergence on `3.0.0`.

## Testing Decisions

**What makes a good test.** Tests exercise the **public surface** of a package — DI extension method outcomes, event-flow behaviour, persistence semantics, telemetry emission — never `internal` plumbing of another package. The split's strict `InternalsVisibleTo` discipline (each package whitelists only its own `.Tests` peer) makes this physically enforceable: a test attempting to reach into a sibling package's internals will not compile.

**Modules tested (every shipped package).**

- `ECommerce.Shared.Kernel.Tests` — telemetry-conventions name constants, `MetricFactory` instantiation, `MessagingOptions` parsing.
- `ECommerce.Shared.EventBus.Tests` — `OutboxUnitOfWork` round-trips, outbox failure tracking, outbox-platform observability emission, event-contract serialisation.
- `ECommerce.Shared.RabbitMq.Tests` — RabbitMQ dead-letter capture integration (against the dockerized broker), `MessagingProviderSwitchTests` RabbitMQ branch.
- `ECommerce.Shared.AzureServiceBus.Tests` — ASB dead-letter capture and publisher, ASB event bus, ASB hosted service lifecycle, topology provisioning policy, emulator compose profile.
- `ECommerce.Shared.DeadLetter.Tests` — `DeadLetterDbContext` origin filter, replayer outcome mapping, discarder, DLQ metric attribution, DLQ-platform observability.
- `ECommerce.Shared.Platform.Tests` — OpenTelemetry options binding, `RequireService` policy enforcement, dual-validator behaviour.
- `ECommerce.Shared.Contracts.Tests` — saga-command contract shape (stub project with `Architecture/LayoutTests.cs` only at start; grows when behaviour is added).
- `ECommerce.Shared.Testing.Qa.Tests` — QA persona seeding extension behaviour.

**Architecture tests in every package's test project.** Each `<pkg>.Tests/Architecture/LayoutTests.cs` asserts: types in `Abstractions/*` have no dependency on `Impl/*`; types in `Impl/*` have no dependency on `Composition/*`; cross-package imports match the locked dependency arrows. These assertions are the NetArchTest mirror of the three Roslyn analyzer rules and exist as a defence in depth.

**Analyzer self-tests.** The Roslyn `ECommerce.Shared.LayoutAnalyzer` project ships with `Microsoft.CodeAnalysis.Testing` unit tests that compile a sample violation snippet and assert the expected diagnostic (`SHA0001`/`SHA0002`/`SHA0003`) is reported with the correct location.

**Prior art for the test shape.** `order-microservice/Order.Service.Tests/Architecture/LayoutTests.cs`, `api-gateway/ApiGateway.Tests/Architecture/LayoutTests.cs`, and the corresponding `Order.Service.LayoutAnalyzer` and `ApiGateway.Service.LayoutAnalyzer` Roslyn projects are the closest reference. The shared-libs analyzer and `LayoutTests` files follow the same NetArchTest fluent style and the same `DiagnosticAnalyzer` registration pattern, parametrised over package names instead of slice names.

**End-to-end gate for the PR 1 (shared-libs) release.** `dotnet build ECommerce.Shared.slnx` clean; `dotnet test ECommerce.Shared.slnx` all green; `dotnet pack -c Release` produces nine `*.3.0.0.nupkg` files; pushing those into `local-nuget-packages/` lets a sample consumer (e.g. order-microservice) run `dotnet restore && dotnet build` against the new version unchanged.

**End-to-end gate per consumer PR (PR 2–10).** `dotnet restore && dotnet build && dotnet test` from the consumer's directory; `docker compose up --build <service>` for broker-dependent services; a publish/consume smoke against one integration event with outbox and telemetry surface visually confirmed.

## Out of Scope

- **Reshaping consumer service code.** Services are already on the default Clean Arch + VSA shape via ADR-0012's sweep. This PRD covers shared-libs only, and a one-line `<PackageReference>` version bump per service.
- **Re-pinning consumers to narrow packages.** The umbrella metapackage means consumers continue to reference `ECommerce.Shared`. Optional opportunistic re-pinning to narrow packages (e.g. Auth dropping to `ECommerce.Shared.Platform` + `ECommerce.Shared.Testing.Qa` only) is a future PR per service, not part of this work.
- **Moving saga commands out of shared.** A future PRD may consider relocating saga commands into a `Saga.Contracts` package owned by `saga-microservice`. This PRD keeps them in `ECommerce.Shared.Contracts` for continuity.
- **Splitting Authentication from the rest of Platform.** Authentication, Observability, HealthChecks, and OpenApi are bundled in `Platform` because they are universally consumed and have no meaningful internal coupling. A future split is possible but is not motivated by current usage patterns.
- **Changing the local NuGet feed location or feed protocol.** `local-nuget-packages/` remains the publish target.
- **Adopting `Central Package Management` (`Directory.Packages.props`) across consumers.** The version-pin sweep edits each service's csproj line directly; central package management for the whole monorepo is a separate, larger initiative.
- **Migrating the documented latent eager-broker bug behind a feature flag.** The version-pin sweep closes it by upgrading every consumer to a post-fix version; no flag is needed.

## Further Notes

- The validation pass surfaced one cross-package compile risk that is *not* visible in today's code because `InternalsVisibleTo` papers over it: `Platform/Observability/OpenTelemetryStartupExtensions.cs` references `internal static` telemetry name constants in `OutboxTelemetry`, `DeadLetterMetrics`, and `RabbitMqTelemetry`. The chosen fix (extract constants into `Kernel/Abstractions/TelemetryConventions/` and promote to `public const`) is implementation decision #34 above and avoids both the dependency-arrow violation and the internal-visibility leak in one move.
- Phase ordering is designed so that every phase leaves the solution buildable. The umbrella's old csproj keeps overflow code temporarily during phases 2–9, then is reduced to a pure metapackage in phase 10. Test redistribution happens in phase 12, after all source-side carving is complete.
- Lockstep versioning is a deliberate departure from independent semver for sibling packages. The rationale (eliminate version-skew bugs like the current 2.18/2.24/2.25 split) is captured in the new ADR 0013 and the new runbook `docs/runbooks/shared-libs-versioning.md`.
- The `shared-libs/CLAUDE.md` §"Broker singletons must register lazy" guidance (CLAUDE.md lines 31–40) continues to apply unchanged after the split; the rule lives in the `ECommerce.Shared.RabbitMq` package's `Composition/` extension methods and the `LayoutAnalyzer` does not regress the lazy-vs-eager check (it is a runtime behaviour, not a layout concern).
