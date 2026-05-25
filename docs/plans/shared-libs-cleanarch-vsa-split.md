# Plan: `ECommerce.Shared` Clean Arch + Vertical Slices split

> Source PRD: [`docs/prd/PRD-Shared-Libs-CleanArch-VSA-Split.md`](../prd/PRD-Shared-Libs-CleanArch-VSA-Split.md)

## Context

`shared-libs/ECommerce.Shared` is the one platform library every service + api-gateway depends on. Today it ships as a single NuGet package (`2.25.0`) with a flat per-capability folder layout and no enforced internal boundaries. Three pains result:

1. **Version skew.** Auth/Basket/Product pinned at `2.18.0`, Order at `2.24.0`, Inventory/Payment/Shipping/Saga/ApiGateway at `2.25.0`. Forces shared-libs to maintain three minor versions in production at once.
2. **Latent eager-broker defect** carried by Auth/Basket/Product (`2.18.0`, pre-fix). Lazy `IRabbitMqConnection` fix shipped in `2.24.0` (commit `dcbc29c`).
3. **Boundary rot hidden by `InternalsVisibleTo`.** `Observability/OpenTelemetryStartupExtensions` reads `internal static` constants from `OutboxTelemetry` + `DeadLetterMetrics`, plus `public` constants from `RabbitMqTelemetry`/`AzureServiceBusTelemetry`. Nothing in build prevents new dependency-arrow violations.

Outcome: split into 8 capability NuGet packages + 1 umbrella metapackage on lockstep `3.0.0`; enforce inner Clean Arch shape (`Abstractions/` + `Impl/` + `Composition/`) via single Roslyn analyzer (`ECommerce.Shared.LayoutAnalyzer`) + per-package NetArchTest `LayoutTests.cs`; sweep all 9 consumer csprojs to umbrella v`3.0.0` (closes eager-broker defect as side effect). Namespaces inside packages stay byte-identical → consumer source untouched.

## Architectural decisions

Durable across all phases.

- **Package partition (8 + 1 umbrella):** `Kernel`, `EventBus` (bundles full Outbox), `RabbitMq`, `AzureServiceBus`, `DeadLetter` (co-locates `MessagingProviderResolver`), `Platform` (Auth+Obs+HC+OpenApi), `Contracts` (11 saga commands), `Testing.Qa`, `ECommerce.Shared` (umbrella, empty csproj).
- **Dependency arrows (locked, enforced by `SHALAY003`):**
  - `Kernel` → nothing
  - `EventBus` → `Kernel`
  - `RabbitMq` → `Kernel`, `EventBus`
  - `AzureServiceBus` → `Kernel`, `EventBus`
  - `DeadLetter` → `Kernel`, `RabbitMq`, `AzureServiceBus`
  - `Platform` → `Kernel`
  - `Contracts` → `Kernel`
  - `Testing.Qa` → `Kernel`
  - `ECommerce.Shared` (umbrella) → all 8
- **Inner shape per package:** `Abstractions/` (ports), `Impl/` (adapters), `Composition/` (DI `AddX()` extensions).
- **Stable namespaces.** Namespaces inside each new package keep current values (e.g. `ECommerce.Shared.Infrastructure.EventBus` lives inside `ECommerce.Shared.EventBus.csproj` but the `namespace` declaration is unchanged). Consumer `using` statements untouched.
- **Lockstep versioning.** `shared-libs/Directory.Build.props` sets `<Version>3.0.0</Version>` for all 9 csprojs. One bump per release, no sibling skew possible.
- **Diagnostic IDs:** `SHALAY001` (Abstractions → Impl forbidden), `SHALAY002` (Impl → Composition forbidden), `SHALAY003` (cross-package import outside allowlist). Matches repo's `<svc>LAY###` convention (`ORDLAY`/`AGWLAY`).
- **Solution shape:** single `shared-libs/ECommerce.Shared.slnx` listing 9 src csprojs + 8 test csprojs + 1 analyzer csproj.
- **Pack cadence:** packs+pushes only at phase 10 release-cut (no interim 3.0.0-preN). Internal phases stay buildable via `<ProjectReference>`.
- **Telemetry conventions in Kernel.** All four `ActivitySourceName`/`MeterName` constants promoted to `public const` in `ECommerce.Shared.Kernel/Abstractions/TelemetryConventions/`. Existing values preserved byte-for-byte (`"ECommerce.Shared.Outbox"`, `"ECommerce.Shared.DeadLetter"`, `"RabbitMQEventBus"`, `"AzureServiceBusEventBus"`). `Platform.Observability.OpenTelemetryStartupExtensions` rewired to read from Kernel.
- **Outbox + DLQ schemas frozen.** `OutboxContext` and `DeadLetterDbContext` (table `dead_letter_messages`) byte-identical post-split — no migrations against any consumer database.
- **Behaviour-preserving public surface.** All `Add*` DI extension method signatures unchanged. `Messaging:Provider=RabbitMQ|AzureServiceBus` switch unchanged. JWKS cache cadence + `RequireService`/`RequireOperator` claim requirements unchanged.

---

## Phase 1: Bootstrap solution shape

**User stories:** 17, 23, 24, 30

### What to build

Lay foundations so subsequent phases are pure moves. Add `shared-libs/Directory.Build.props` with `<Version>3.0.0</Version>` + `<TargetFramework>net10.0</TargetFramework>` + shared `IsPackable=true`. Add 8 empty capability csprojs under `shared-libs/ECommerce.Shared.<Package>/` each with `Abstractions/`, `Impl/`, `Composition/` folders (one `.placeholder` file each so dirs commit). Add `shared-libs/ECommerce.Shared.LayoutAnalyzer/` Roslyn project (`netstandard2.0`, `IsRoslynComponent=true`, Roslyn 4.8.0, `RegisterSyntaxTreeAction`) with rule SHALAY001/2/3 defined but skeleton (no allowlist data yet — analyzer is wired but inert). Update `shared-libs/ECommerce.Shared.slnx` to list all 9 src csprojs + analyzer; keep existing tests csproj. Wire analyzer into every csproj via `<ProjectReference … OutputItemType="Analyzer" ReferenceOutputAssembly="false" />`. Existing `ECommerce.Shared.csproj` (umbrella) keeps all current source temporarily — it remains the only package with code.

### Acceptance criteria

- [ ] `dotnet build shared-libs/ECommerce.Shared.slnx` clean
- [ ] `dotnet test shared-libs/ECommerce.Shared.slnx` all green (20 existing tests in flat project)
- [ ] All 9 src csprojs visible in `.slnx`; `Directory.Build.props` resolves `<Version>3.0.0</Version>` everywhere
- [ ] Analyzer csproj compiles; SHALAY001/2/3 diagnostic descriptors registered (rules don't trip yet because production code still lives in umbrella)
- [ ] No consumer csproj changes; no `local-nuget-packages/` push

---

## Phase 2: Carve Kernel + telemetry conventions promotion

**User stories:** 9, 10, 11, 15, 34

### What to build

Move primitives into `ECommerce.Shared.Kernel/`: `Event` base class → `Abstractions/Event.cs`; `MessagingOptions` → `Abstractions/MessagingOptions.cs`; `MetricFactory` → `Impl/MetricFactory.cs`. Create `Kernel/Abstractions/TelemetryConventions/` with four `public static class` types holding `public const string ActivitySourceName` + `MeterName`: `OutboxTelemetryNames`, `DeadLetterTelemetryNames`, `RabbitMqTelemetryNames`, `AzureServiceBusTelemetryNames`. Existing `internal static` constants in `OutboxTelemetry`/`DeadLetterMetrics` and `public` constants in `RabbitMqTelemetry`/`AzureServiceBusTelemetry` rewritten to reference the Kernel constants (preserve byte-identical values: `"ECommerce.Shared.Outbox"`, `"ECommerce.Shared.DeadLetter"`, `"RabbitMQEventBus"`, `"AzureServiceBusEventBus"`). Rewrite `Observability/OpenTelemetryStartupExtensions.cs` `.AddSource(...)` / `.AddMeter(...)` calls to read from Kernel — resolves the `InternalsVisibleTo`-hidden cross-boundary read. Activate SHALAY001/2 enforcement on Kernel namespace (analyzer allowlist data populated for `Kernel`).

### Acceptance criteria

- [ ] `dotnet build` clean; `dotnet test` all green
- [ ] `OpenTelemetryOptionsTests` still pass without alteration (constant values unchanged)
- [ ] Source/Meter names emitted by a smoke trace match pre-refactor (`"ECommerce.Shared.Outbox"` etc.) — grep production code shows no remaining cross-package read of `OutboxTelemetry.MeterName` from Observability
- [ ] SHALAY001 trips on a deliberate `Abstractions/Event.cs` → `Impl/MetricFactory` import added then reverted (analyzer self-test scratchpad)

---

## Phase 3: Carve Contracts

**User stories:** 7, 9, 10, 11

### What to build

Move all 11 saga command classes from `ECommerce.Shared/IntegrationEvents/Commands/` to `ECommerce.Shared.Contracts/Abstractions/` (they're POCOs, no `Impl/` needed; `Composition/` empty placeholder). Namespaces stay `ECommerce.Shared.IntegrationEvents.Commands`. Activate SHALAY003 allowlist for `Contracts` → `Kernel`-only.

### Acceptance criteria

- [ ] `dotnet build` clean; `dotnet test` all green
- [ ] No `using` changes anywhere in saga-microservice / inventory / payment / shipping / order
- [ ] Analyzer rejects (test snippet) a synthetic `Contracts` → `EventBus` reference

---

## Phase 4: Carve Testing.Qa

**User stories:** 8, 9, 10, 11

### What to build

Move `Qa/QaPersonas.cs` → `ECommerce.Shared.Testing.Qa/Abstractions/`; `Qa/QaSeedingExtensions.cs` → `Composition/`. Activate SHALAY003 allowlist for `Testing.Qa` → `Kernel`-only.

### Acceptance criteria

- [ ] `dotnet build` clean; `dotnet test` all green (including existing `QaSeedingExtensionsTests`)

---

## Phase 5: Carve EventBus + Outbox bundle

**User stories:** 2, 9, 10, 11, 18, 20

### What to build

Move into `ECommerce.Shared.EventBus/`: `Infrastructure/EventBus/Abstractions/IEventBus.cs` + `IEventHandler.cs` → `Abstractions/`; `EventBusOptions.cs` → `Abstractions/`; `Event.cs` + `EventBusHandlerExtensions.cs` → `Composition/`. Bundle full Outbox capability: move `Infrastructure/Outbox/*` (context, `OutboxBackgroundService`, `OutboxUnitOfWork`, `IOutboxStore`, `IOutboxUnitOfWork`, `OutboxContextFactory`, `OutboxOptions`, `Migrations/`, `Models/`) into `Abstractions/Outbox/` + `Impl/Outbox/` per Clean Arch split. `OutboxStartupExtensions.cs` → `Composition/Outbox/`. `OutboxTelemetry.cs` retains class but reads names from Kernel (already done phase 2). Activate SHALAY allowlist: `EventBus` → `Kernel`.

### Acceptance criteria

- [ ] `dotnet build` clean
- [ ] `OutboxFailureTrackingTests`, `OutboxPlatformObservabilityTests`, `OutboxUnitOfWorkTests`, `EventContractTests` all green unchanged
- [ ] `OutboxContext` EF model snapshot byte-identical to pre-phase snapshot (no model-change → no migration)

---

## Phase 6: Carve RabbitMq

**User stories:** 3, 9, 10, 11, 15, 21, 22

### What to build

Move `Infrastructure/RabbitMq/*` into `ECommerce.Shared.RabbitMq/`: `IRabbitMqConnection.cs`, `RabbitMqConnection.cs`, `RabbitMqEventBus.cs`, `RabbitMqHostedService.cs`, `RabbitMqOptions.cs`, `RabbitMqTopology.cs` → split across `Abstractions/`/`Impl/`. `RabbitMqStartupExtensions.cs` → `Composition/` — must preserve lazy `IRabbitMqConnection` registration verbatim (the `dcbc29c` fix). `RabbitMqTelemetry.cs` reads source name from Kernel (already done phase 2). Activate SHALAY allowlist: `RabbitMq` → `Kernel`, `EventBus`.

### Acceptance criteria

- [ ] `dotnet build` clean; `dotnet test` all green (`RabbitMqDeadLetterIntegrationTests`, `MessagingProviderSwitchTests` RabbitMq branch)
- [ ] `RabbitMqStartupExtensions` registers `IRabbitMqConnection` via factory (not eager singleton) — grep confirms lazy shape preserved
- [ ] `ActivitySource` name still emits `"RabbitMQEventBus"`

---

## Phase 7: Carve AzureServiceBus

**User stories:** 4, 9, 10, 11, 15, 21

### What to build

Move `Infrastructure/AzureServiceBus/*` (9 files) into `ECommerce.Shared.AzureServiceBus/` mirroring RabbitMq's shape. Activate SHALAY allowlist: `AzureServiceBus` → `Kernel`, `EventBus`.

### Acceptance criteria

- [ ] `dotnet build` clean
- [ ] `AzureServiceBusEventBusTests`, `AzureServiceBusHostedServiceTests`, `AzureServiceBusTopologyProvisioningPolicyTests`, `AsbEmulatorComposeProfileTests` all green
- [ ] `ActivitySource` name still emits `"AzureServiceBusEventBus"`

---

## Phase 8: Carve DeadLetter + Messaging provider resolver

**User stories:** 5, 9, 10, 11, 15, 19, 21

### What to build

Move `Infrastructure/DeadLetter/*` into `ECommerce.Shared.DeadLetter/`: `DeadLetterDbContext.cs` + `Models/` + `Migrations/` → `Impl/`; `IDeadLetterCapture.cs`, `IDeadLetterStore.cs` → `Abstractions/`; `DeadLetterDiscarder.cs`, `DeadLetterReplayer.cs`, `RabbitMqDeadLetterCapture.cs`, `AzureServiceBusDeadLetterCapture.cs`, publisher impls → `Impl/`. Co-locate `Infrastructure/Messaging/MessagingProviderResolver.cs` → `DeadLetter/Impl/` (DLQ is the only cross-broker consumer). `Infrastructure/Messaging/MessagingStartupExtensions.cs` → `DeadLetter/Composition/`. `DeadLetterMetrics.cs` reads names from Kernel (already done phase 2). Activate SHALAY allowlist: `DeadLetter` → `Kernel`, `RabbitMq`, `AzureServiceBus`.

### Acceptance criteria

- [ ] `dotnet build` clean
- [ ] `DeadLetterDbContextOriginFilterTests`, `DeadLetterReplayerTests`, `DeadLetterDiscarderTests`, `DlqMetricAttributionTests`, `DeadLetterPlatformObservabilityTests`, `AzureServiceBusDeadLetterCaptureTests`, `AzureServiceBusDeadLetterPublisherTests` all green
- [ ] `dead_letter_messages` EF model snapshot byte-identical

---

## Phase 9: Carve Platform (Auth + Obs + HC + OpenApi)

**User stories:** 6, 9, 10, 11, 15, 16, 17

### What to build

Move into `ECommerce.Shared.Platform/`: `Authentication/` (`AuthOptions.cs` → `Abstractions/`, `AuthorizationPolicies.cs` → `Abstractions/`, `AuthenticationExtensions.cs` → `Composition/`); `Observability/` (`OpenTelemetryOptions.cs` + `OpenTelemetryMessagingConventions.cs` → `Abstractions/`, `Metrics/` → `Impl/`, `OpenTelemetryStartupExtensions.cs` → `Composition/`); `HealthChecks/` (4 health checks → `Impl/`, `HealthCheckStartupExtensions.cs` → `Composition/`); `OpenApi/OpenApiStartupExtensions.cs` → `Composition/`. Activate SHALAY allowlist: `Platform` → `Kernel`-only.

### Acceptance criteria

- [ ] `dotnet build` clean
- [ ] `OpenTelemetryOptionsTests`, `DualValidatorTests`, `RequireServicePolicyTests` all green
- [ ] JWKS endpoint cache cadence + key set unchanged (verify against existing JWT validation tests)
- [ ] `RequireService` rejects user tokens; `RequireOperator` requires `Operator` claim — both behaviours unchanged

---

## Phase 10: Reduce umbrella to pure metapackage; v3.0.0 release-cut

**User stories:** 13, 14, 24, 25, 26

### What to build

Strip all remaining code + folders from `ECommerce.Shared.csproj`. Final umbrella csproj contains only:

```xml
<PackageReference Include="ECommerce.Shared.Kernel" Version="3.0.0" />
<PackageReference Include="ECommerce.Shared.EventBus" Version="3.0.0" />
<PackageReference Include="ECommerce.Shared.RabbitMq" Version="3.0.0" />
<PackageReference Include="ECommerce.Shared.AzureServiceBus" Version="3.0.0" />
<PackageReference Include="ECommerce.Shared.DeadLetter" Version="3.0.0" />
<PackageReference Include="ECommerce.Shared.Platform" Version="3.0.0" />
<PackageReference Include="ECommerce.Shared.Contracts" Version="3.0.0" />
<PackageReference Include="ECommerce.Shared.Testing.Qa" Version="3.0.0" />
```

Run `dotnet pack -c Release shared-libs/ECommerce.Shared.slnx`. Glob-copy all `**/bin/Release/*.3.0.0.nupkg` (9 files) into `local-nuget-packages/`. Smoke-test: pick Inventory (lowest-risk consumer already at 2.25.0), bump its `<PackageReference>` to 3.0.0, run `dotnet restore && dotnet build && dotnet test`, revert. Do NOT keep the Inventory bump in this phase — phase 13 owns consumer sweep.

### Acceptance criteria

- [ ] `ECommerce.Shared.csproj` contains zero `.cs` files (verify with glob)
- [ ] `dotnet pack` emits 9 `*.3.0.0.nupkg` files
- [ ] All 9 nupkgs present in `local-nuget-packages/`
- [ ] Inventory smoke (bump → build → test → revert) green
- [ ] Existing 20 tests in flat tests project still green (test redistribution is phase 11)

---

## Phase 11: Test redistribution + per-package LayoutTests + analyzer self-tests

**User stories:** 12, 23, 27, 28

### What to build

Dissolve flat `ECommerce.Shared.Tests/` into 8 per-package test csprojs under `shared-libs/tests/`:

- `ECommerce.Shared.Kernel.Tests` — telemetry-conventions value tests, `MetricFactory`, `MessagingOptions` parsing
- `ECommerce.Shared.EventBus.Tests` — `OutboxUnitOfWorkTests`, `OutboxFailureTrackingTests`, `OutboxPlatformObservabilityTests`, `EventContractTests`
- `ECommerce.Shared.RabbitMq.Tests` — `RabbitMqDeadLetterIntegrationTests`, `MessagingProviderSwitchTests` (RabbitMq branch)
- `ECommerce.Shared.AzureServiceBus.Tests` — `AzureServiceBusEventBusTests`, `AzureServiceBusHostedServiceTests`, `AzureServiceBusTopologyProvisioningPolicyTests`, `AsbEmulatorComposeProfileTests`
- `ECommerce.Shared.DeadLetter.Tests` — `DeadLetterDbContextOriginFilterTests`, `DeadLetterReplayerTests`, `DeadLetterDiscarderTests`, `DlqMetricAttributionTests`, `DeadLetterPlatformObservabilityTests`, `AzureServiceBusDeadLetterCaptureTests`, `AzureServiceBusDeadLetterPublisherTests`
- `ECommerce.Shared.Platform.Tests` — `OpenTelemetryOptionsTests`, `DualValidatorTests`, `RequireServicePolicyTests`
- `ECommerce.Shared.Contracts.Tests` — stub with only `Architecture/LayoutTests.cs` at start
- `ECommerce.Shared.Testing.Qa.Tests` — `QaSeedingExtensionsTests`

Each test csproj single `<ProjectReference>` to its own package only. Each src package csproj gains `<InternalsVisibleTo Include="ECommerce.Shared.<Pkg>.Tests" />` and revokes the old flat allowlist. Add `Architecture/LayoutTests.cs` to every test csproj (NetArchTest 1.3.2 fluent style mirroring SHALAY001/2/3 — `Types.InAssembly(...).That().ResideInNamespaceStartingWith("...Abstractions").ShouldNot().HaveDependencyOn("...Impl").GetResult()`).

Add `ECommerce.Shared.LayoutAnalyzer.Tests/` using `Microsoft.CodeAnalysis.Testing` with one snippet per diagnostic ID asserting the analyzer fires with correct location + ID.

Delete old `ECommerce.Shared.Tests/` csproj + folder. Update `shared-libs/ECommerce.Shared.slnx` to list all 8 new test csprojs + analyzer-tests csproj.

### Acceptance criteria

- [ ] `dotnet test shared-libs/ECommerce.Shared.slnx` all green
- [ ] No test csproj references more than one production package via `<ProjectReference>`
- [ ] Synthetic `Abstractions/Foo.cs` adding `using …Impl;` fails build with `SHALAY001`; reverted
- [ ] Synthetic cross-package reference fails build with `SHALAY003`
- [ ] Analyzer self-tests assert SHALAY001/2/3 emitted with expected `Diagnostic.Location`

---

## Phase 12: Docs sweep

**User stories:** 26, 30, 31, 32

### What to build

Files to write/edit:

- New `docs/adr/0013-shared-libs-multi-package-split.md` — captures: 8-package partition rationale, umbrella metapackage decision, lockstep semver, Abstractions/Impl/Composition convention, telemetry-conventions-in-Kernel decision. Links back to ADR-0011 + ADR-0012.
- New `docs/runbooks/shared-libs-versioning.md` — bump-and-publish workflow (edit `Directory.Build.props`, `dotnet pack`, glob-push), lockstep semver rule, consumer version-pin sweep procedure (9 PRs).
- Rewrite `shared-libs/CLAUDE.md` §"Pack + publish flow" — `dotnet pack -c Release ECommerce.Shared.slnx` whole-solution pack + glob push of all `**/bin/Release/*.3.0.0.nupkg`.
- Update `shared-libs/CLAUDE.md` §"Version pinning history" — placeholder for post-sweep convergence record (phase 13 fills in actual value).
- Update root `CLAUDE.md` §"Repo shape" "Shared libraries" pointer line + cross-reference to new ADR/runbook.

### Acceptance criteria

- [ ] ADR + runbook exist and link from `docs/adr/README.md` (if exists) and root `CLAUDE.md`
- [ ] `shared-libs/CLAUDE.md` Pack+publish recipe runs verbatim and produces the 9 nupkgs in `local-nuget-packages/`
- [ ] Markdown lint clean (existing pre-commit doc rules)

---

## Phase 13: Consumer version-pin sweep (9 ordered sub-PRs)

**User stories:** 13, 14, 22, 29, 33, 35

### What to build

One-line `<PackageReference Include="ECommerce.Shared" Version="..." />` bump per consumer csproj from current pin to `3.0.0`. Ordered low-risk first so any v3.0.0 ABI regression surfaces on least-coupled consumer first.

| Sub-PR | Csproj | From | To |
|---|---|---|---|
| 1 | `auth-microservice/Auth.Service/Auth.Service.csproj` | 2.18.0 | 3.0.0 |
| 2 | `basket-microservice/Basket.Service/Basket.Service.csproj` | 2.18.0 | 3.0.0 |
| 3 | `product-microservice/Product.Service/Product.Service.csproj` | 2.18.0 | 3.0.0 |
| 4 | `order-microservice/Order.Service/Order.Service.csproj` | 2.24.0 | 3.0.0 |
| 5 | `inventory-microservice/Inventory.Service/Inventory.Service.csproj` | 2.25.0 | 3.0.0 |
| 6 | `payment-microservice/Payment.Service/Payment.Service.csproj` | 2.25.0 | 3.0.0 |
| 7 | `shipping-microservice/Shipping.Service/Shipping.Service.csproj` | 2.25.0 | 3.0.0 |
| 8 | `saga-microservice/Saga.Service/Saga.Service.csproj` | 2.25.0 | 3.0.0 |
| 9 | `api-gateway/ApiGateway/ApiGateway.csproj` | 2.25.0 | 3.0.0 |

Per sub-PR loop: edit version line → `dotnet restore && dotnet build && dotnet test` from service dir → for broker-dependent services (basket onward) `docker compose up --build <service>` + one publish/consume smoke against an integration event with outbox + telemetry visually confirmed. Auth has no `MessagingProviderBootTests`-equivalent gate (along with saga + api-gateway) — for those three, add a temporary boot-time assertion in PR (or rely on integration test) to confirm the lazy `IRabbitMqConnection` fix applies. Sub-PR 1 (auth) and sub-PR 2 (basket) and sub-PR 3 (product) sweep up the pre-fix eager-broker defect.

After sub-PR 9 merges, final commit updates `shared-libs/CLAUDE.md` §"Version pinning history" to record convergence on `3.0.0`.

### Acceptance criteria

- [ ] All 9 sub-PRs merged in stated order
- [ ] `grep -r 'ECommerce.Shared.*Version="2' .` returns zero hits across the monorepo
- [ ] `dotnet build` clean for every solution
- [ ] `docker compose up --build` succeeds for full stack; saga end-to-end smoke (`OrderCreatedEvent` → `ReserveStockCommand` → ... → `ConfirmOrderCommand`) green
- [ ] DLQ poller still populates `dead_letter_messages` on a forced failure
- [ ] Operator endpoint `/operator/api/failures*` still responds with same auth requirements
- [ ] No `--no-verify` used in any commit
- [ ] `shared-libs/CLAUDE.md` "Version pinning history" reflects `3.0.0` everywhere

---

## End-to-end verification (gate before declaring done)

Run from repo root after phase 13:

1. `dotnet build shared-libs/ECommerce.Shared.slnx` — clean.
2. `dotnet test shared-libs/ECommerce.Shared.slnx` — all per-package tests + analyzer self-tests green.
3. For each consumer dir: `dotnet build && dotnet test` — green.
4. `docker compose up --build` from repo root — full stack healthy; hit `/health/ready` on every service.
5. End-to-end saga smoke: place an order via api-gateway → confirm `Confirmed` state in saga DB → confirm `dead_letter_messages` empty.
6. Telemetry smoke: grep Jaeger/Prometheus scrape for `ActivitySource` names `"ECommerce.Shared.Outbox"`, `"ECommerce.Shared.DeadLetter"`, `"RabbitMQEventBus"`, `"AzureServiceBusEventBus"` — all present.
7. Force a DLQ event (stop a consumer, publish a poison message, restart) → confirm `dlq_messages_total` increments + replay via operator endpoint succeeds with `dlq.replay` span emitted carrying original `CorrelationId`.
8. `grep -r "InternalsVisibleTo" shared-libs/` — each src package whitelists only its own `.Tests` peer; no cross-package leakage.

## Critical files to modify

- `shared-libs/Directory.Build.props` (new, phase 1)
- `shared-libs/ECommerce.Shared.slnx` (phases 1, 11)
- `shared-libs/ECommerce.Shared.LayoutAnalyzer/LayoutAnalyzer.cs` (new, phase 1; rules activated per-phase 2–9)
- `shared-libs/ECommerce.Shared.<Pkg>/*.csproj` × 8 (new, phase 1)
- `shared-libs/ECommerce.Shared/ECommerce.Shared.csproj` (reduced to pure metapackage, phase 10)
- `shared-libs/tests/ECommerce.Shared.<Pkg>.Tests/*.csproj` × 8 (new, phase 11)
- `shared-libs/ECommerce.Shared/Infrastructure/Outbox/OutboxTelemetry.cs` (phase 2 — reads from Kernel)
- `shared-libs/ECommerce.Shared/Infrastructure/DeadLetter/DeadLetterMetrics.cs` (phase 2)
- `shared-libs/ECommerce.Shared/Infrastructure/RabbitMq/RabbitMqTelemetry.cs` (phase 2)
- `shared-libs/ECommerce.Shared/Infrastructure/AzureServiceBus/AzureServiceBusTelemetry.cs` (phase 2)
- `shared-libs/ECommerce.Shared/Observability/OpenTelemetryStartupExtensions.cs` (phase 2 — rewired to Kernel)
- `docs/adr/0013-shared-libs-multi-package-split.md` (new, phase 12)
- `docs/runbooks/shared-libs-versioning.md` (new, phase 12)
- `shared-libs/CLAUDE.md` (phases 12, 13)
- `CLAUDE.md` (root, phase 12)
- 9 consumer csprojs listed in phase 13 table

## Existing functions/utilities to reuse

- Roslyn analyzer pattern: `order-microservice/Order.Service.LayoutAnalyzer/LayoutAnalyzer.cs` + `api-gateway/ApiGateway.Service.LayoutAnalyzer/LayoutAnalyzer.cs` — copy structure, parameterize over package names instead of slice names. `netstandard2.0`, `IsRoslynComponent=true`, Roslyn 4.8.0, `RegisterSyntaxTreeAction`.
- NetArchTest layout test shape: `order-microservice/Order.Tests/Architecture/LayoutTests.cs` + `api-gateway/ApiGateway.Tests/Architecture/LayoutTests.cs` — copy fluent style.
- Analyzer wire-up in consumer csproj: `<ProjectReference … OutputItemType="Analyzer" ReferenceOutputAssembly="false" />` — pattern from `Order.Service.csproj`.
- Lazy `IRabbitMqConnection` registration shape: `shared-libs/ECommerce.Shared/Infrastructure/RabbitMq/RabbitMqStartupExtensions.cs` (post-`dcbc29c`) — preserve verbatim in phase 6.
- `MessagingProviderBootTests` shape: copy from `basket-microservice/Basket.Tests/Infrastructure/MessagingProviderBootTests.cs` (or order/inventory/payment/shipping equivalent) when adding the gate to auth/saga/api-gateway in phase 13.

## Out of scope (per PRD)

- Reshaping consumer service code (already CleanArch+VSA via ADR-0012)
- Re-pinning consumers to narrow packages (future opportunistic PRs)
- Moving saga commands out of `Contracts` package
- Splitting `Platform` (Auth from Obs/HC/OpenApi)
- Changing `local-nuget-packages/` location/protocol
- Adopting `Central Package Management` monorepo-wide
- Feature-flagging the eager-broker bug (sweep upgrades all consumers past it)
