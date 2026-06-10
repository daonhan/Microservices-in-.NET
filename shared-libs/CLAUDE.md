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

The umbrella `ECommerce.Shared.<Version>.nupkg` carries the nine sub-package `<PackageDependency>`s at the same version. It remains a compatibility/prototype package for deliberate broad consumption. Production services should use direct capability package references so their csproj communicates the shared surface they actually use.

Consumers see no change until version bump + new `.nupkg`s in feed. When packing a new shared version, confirm the `.nupkg`s in `local-nuget-packages/` were built **after** the relevant source commit (older nupkgs sharing a version number have been observed). If a consumer build behaves unexpectedly, clear the consumer's NuGet HTTP cache (`dotnet nuget locals http-cache --clear`).

## Narrow package selection

Choose the smallest direct capability set that matches the service. Keep all direct shared-libs package references on the same lockstep version as [`Directory.Build.props`](Directory.Build.props). Do not add `ECommerce.Shared.RabbitMq` or `ECommerce.Shared.AzureServiceBus` to a production service just to select a broker; `ECommerce.Shared.Messaging` owns provider-aware composition. Do not add `ECommerce.Shared.DeadLetter` unless the service owns DLQ capture, storage, replay, or discard; today that is only API Gateway.

| Consumer shape | Direct packages |
|---|---|
| Auth-only service | `ECommerce.Shared.Platform`, `ECommerce.Shared.Testing.Qa` |
| Publisher/subscriber without shared saga commands | `ECommerce.Shared.Platform`, `ECommerce.Shared.EventBus`, `ECommerce.Shared.Messaging`, `ECommerce.Shared.Testing.Qa` |
| Saga participant or orchestrator using shared commands | `ECommerce.Shared.Platform`, `ECommerce.Shared.EventBus`, `ECommerce.Shared.Messaging`, `ECommerce.Shared.Contracts`, `ECommerce.Shared.Testing.Qa` |
| Gateway/operator DLQ owner | `ECommerce.Shared.Platform`, `ECommerce.Shared.Messaging`, `ECommerce.Shared.DeadLetter` |

`Program.cs` extension methods come from those packages: `AddSqlServerDatastore`, `AddOutbox`, `AddPlatformEventBus`, `AddPlatformEventPublisher`, `AddPlatformSubscriberService`, `AddEventHandler<TEvent,THandler>`, `AddPlatformObservability`, `AddPlatformHealthChecks`, `AddPlatformOpenApi`, `AddJwtAuthentication`, `AddRequireOperatorPolicy`, `AddRequireServicePolicy`.

Capability-to-package mapping (namespaces unchanged from pre-split):

- `ECommerce.Shared.Kernel` — `Event` base, telemetry name constants under `Kernel/Abstractions/TelemetryConventions/`, `MessagingOptions`, `MetricFactory`.
- `ECommerce.Shared.EventBus` — `IEventBus` + the entire Outbox capability (`OutboxBackgroundService`, `OutboxUnitOfWork`, migrations).
- `ECommerce.Shared.RabbitMq` / `ECommerce.Shared.AzureServiceBus` — provider-specific broker adapters.
- `ECommerce.Shared.Messaging` — `Messaging:Provider` selects RabbitMQ by default or Azure Service Bus; owns `MessagingProviderResolver`, `AddPlatformEventBus`, `AddPlatformEventPublisher`, and `AddPlatformSubscriberService`.
- `ECommerce.Shared.DeadLetter` — DLQ capture/publisher/replayer/discarder + provider-specific DLQ adapters.
- `ECommerce.Shared.Platform` — Authentication + Observability + HealthChecks + OpenApi bundled.
- `ECommerce.Shared.Contracts` — saga command POCOs.
- `ECommerce.Shared.Testing.Qa` — `QaPersonas` + `QaSeedingExtensions`.

New cross-cutting concerns belong in whichever sub-package matches; if none matches, add another capability package via the runbook's checklist.

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
- Narrow-package convergence: all production consumers pin direct capability packages at `3.1.0`; no production service directly references the umbrella. API Gateway is the only production consumer with a direct `ECommerce.Shared.DeadLetter` reference.
- Azure Monitor distro release: `3.2.0` — `ECommerce.Shared.Platform` adopts the Azure Monitor OpenTelemetry Distro (`Azure.Monitor.OpenTelemetry.AspNetCore`, replacing the raw `Azure.Monitor.OpenTelemetry.Exporter`) on the `Exporter=AzureMonitor` branch; the OTLP branch is unchanged. Minor bump (no public-surface change); all nine consumers swept to `3.2.0`.
