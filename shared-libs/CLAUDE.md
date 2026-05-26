# Shared libraries — notes

`ECommerce.Shared` is consumed as a NuGet package (not project ref). Local feed: `local-nuget-packages/` at the repo root (gitignored).

Since [ADR-0013](../docs/adr/0013-shared-libs-multi-package-split.md), plus the Messaging extraction, the library ships as **nine capability packages plus one umbrella metapackage** on lockstep `<Version>` defined in [`Directory.Build.props`](Directory.Build.props). Every release bumps that one place and packs all ten `.nupkg`s together. Bump-and-publish + consumer-sweep procedure: [`docs/runbooks/shared-libs-versioning.md`](../docs/runbooks/shared-libs-versioning.md).

## Pack + publish flow

After edits, from the repo root:

```bash
# 1. Build + test the whole solution.
dotnet build shared-libs/ECommerce.Shared.slnx
dotnet test shared-libs/ECommerce.Shared.slnx

# 2. Bump <Version> in shared-libs/Directory.Build.props (single source of truth).

# 3. Pack the whole solution — emits 10 *.<Version>.nupkg (one per src csproj).
dotnet pack -c Release shared-libs/ECommerce.Shared.slnx

# 4. Glob-copy all 10 nupkgs into the local feed.
cp shared-libs/**/bin/Release/*.<Version>.nupkg local-nuget-packages/
```

The umbrella `ECommerce.Shared.<Version>.nupkg` carries the nine sub-package `<PackageDependency>`s at the same version. Consumers `restore` against the umbrella alone and pull the full set transitively.

Consumers see no change until version bump + new `.nupkg`s in feed. When packing a new shared version, confirm the `.nupkg`s in `local-nuget-packages/` were built **after** the relevant source commit (older nupkgs sharing a version number have been observed). If a consumer build behaves unexpectedly, clear the consumer's NuGet HTTP cache (`dotnet nuget locals http-cache --clear`).

## Composition extensions

Each service's `Program.cs` uses the umbrella's transitive extensions: `AddSqlServerDatastore`, `AddOutbox`, `AddPlatformEventBus`, `AddPlatformEventPublisher`, `AddPlatformSubscriberService`, `AddEventHandler<TEvent,THandler>`, `AddPlatformObservability`, `AddPlatformHealthChecks`, `AddPlatformOpenApi`, `AddJwtAuthentication`, `AddRequireOperatorPolicy`, `AddRequireServicePolicy`.

Capability-to-package mapping (namespaces unchanged from pre-split):

- `ECommerce.Shared.Kernel` — `Event` base, telemetry name constants under `Kernel/Abstractions/TelemetryConventions/`, `MessagingOptions`, `MetricFactory`.
- `ECommerce.Shared.EventBus` — `IEventBus` + the entire Outbox capability (`OutboxBackgroundService`, `OutboxUnitOfWork`, migrations).
- `ECommerce.Shared.RabbitMq` / `ECommerce.Shared.AzureServiceBus` — provider-specific broker adapters.
- `ECommerce.Shared.Messaging` — `Messaging:Provider` selects RabbitMQ by default or Azure Service Bus; owns `MessagingProviderResolver`, `AddPlatformEventBus`, `AddPlatformEventPublisher`, and `AddPlatformSubscriberService`.
- `ECommerce.Shared.DeadLetter` — DLQ capture/publisher/replayer/discarder + provider-specific DLQ adapters.
- `ECommerce.Shared.Platform` — Authentication + Observability + HealthChecks + OpenApi bundled.
- `ECommerce.Shared.Contracts` — saga command POCOs.
- `ECommerce.Shared.Testing.Qa` — `QaPersonas` + `QaSeedingExtensions`.

New cross-cutting concerns belong in whichever sub-package matches; if none matches, add a ninth package via the runbook's checklist.

## Inner shape — `Abstractions/` + `Impl/` + `Composition/`

Every sub-package keeps the same inner triad (mirrors ADR-0011's per-service shape, parametrised over package names). Boundaries enforced by the Roslyn analyzer [`ECommerce.Shared.LayoutAnalyzer`](ECommerce.Shared.LayoutAnalyzer/) at build time (`SHALAY001`, `SHALAY002`, `SHALAY003`) and by `Architecture/LayoutTests.cs` (NetArchTest) in every per-package test csproj at test time. Cross-package import rules live in `LayoutAnalyzer.cs`'s `CrossPackageAllowlist` dictionary.

## Broker singletons must register lazy

```csharp
// good
AddSingleton<IRabbitMqConnection>(_ => new RabbitMqConnection(opts));

// bad — opens socket during Program.Main, before WebApplicationFactory.ConfigureWebHost can swap stubs
AddSingleton<IRabbitMqConnection>(new RabbitMqConnection(opts));
```

Eager registration breaks boot tests like `Inventory.Tests.MessagingProviderBootTests` in any sandbox without a reachable broker. The lazy form lives in [`ECommerce.Shared.RabbitMq/Composition/RabbitMqStartupExtensions.cs`](ECommerce.Shared.RabbitMq/Composition/RabbitMqStartupExtensions.cs) since the Phase 6 carve; the rule applies unchanged.

## Version pinning history

- Lazy broker fix shipped in `ECommerce.Shared` ≥ `2.24.0` (commit `dcbc29c`).
- Lockstep multi-package release: `3.0.0` ships eight sub-packages + one umbrella together ([ADR-0013](../docs/adr/0013-shared-libs-multi-package-split.md)).
- Messaging extraction release: `3.1.0` ships nine sub-packages + one umbrella together and moves provider-aware composition out of DeadLetter.
- **Convergence target**: every consumer pins the current shared-libs lockstep version after the narrow-package sweep (one PR per consumer, low-risk-first order — runbook lists the order). Until that sweep lands, current consumer pins are:
  - Auth / Basket / Product — `2.18.0` (pre-fix, carries latent eager-broker defect)
  - Order — `2.24.0`
  - Inventory / Payment / Shipping / Saga / ApiGateway — `2.25.0`

Post-sweep this section becomes a single line ("All consumers pin `3.0.0`") plus the Phase 13 issue link for history.
