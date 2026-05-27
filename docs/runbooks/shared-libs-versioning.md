# Shared libraries versioning — bump, publish, sweep

This runbook is the bump-and-publish workflow for `shared-libs/ECommerce.Shared` after [ADR-0013](../adr/0013-shared-libs-multi-package-split.md) split the library into capability packages plus one umbrella metapackage. After the Messaging extraction, every shared-libs release ships nine capability `.nupkg` files plus the umbrella at a single lockstep version. Production consumers should use direct capability packages; the umbrella stays available for compatibility, prototypes, and deliberate broad consumption.

## Layout the runbook assumes

- Ten source csprojs under [`shared-libs/`](../../shared-libs/) — nine capability packages plus the umbrella.
- One `<Version>` in [`shared-libs/Directory.Build.props`](../../shared-libs/Directory.Build.props), inherited by every csproj in the solution.
- One solution at [`shared-libs/ECommerce.Shared.slnx`](../../shared-libs/ECommerce.Shared.slnx) listing every src + test + analyzer csproj.
- Local file-based NuGet feed at [`local-nuget-packages/`](../../local-nuget-packages/) at the repo root (gitignored), referenced by every consumer csproj's `nuget.config` as `../local-nuget-packages`.

## Lockstep semver rule

All ten packages share `<Version>` via `shared-libs/Directory.Build.props`. A release bumps that value once. Sibling-version skew (e.g. `EventBus.3.1.0` + `RabbitMq.3.0.0`) is structurally impossible. The rule is deliberate — eliminating the prior 2.18 / 2.24 / 2.25 consumer-side skew was the motivating decision for the split. If per-package release cadence becomes necessary later, the rule is recorded in ADR-0013 and can be revisited via a follow-up ADR.

The corollary for bump granularity:

- **Patch** — bugfix that touches one or more sub-packages with no public-surface change. Bump all ten.
- **Minor** — additive change (new DI extension, new options field with a default) with no breakage. Bump all ten.
- **Major** — public-surface break (rename, signature change, removed extension). Bump all ten. Roll the consumer sweep procedure below.

## Bump-and-publish workflow

From repo root.

```bash
# 1. Edit the single source-of-truth version.
$EDITOR shared-libs/Directory.Build.props          # bump <Version>x.y.z</Version>

# 2. Build + test the whole solution.
dotnet build shared-libs/ECommerce.Shared.slnx
dotnet test shared-libs/ECommerce.Shared.slnx

# 3. Pack the whole solution. Produces 10 *.<Version>.nupkg
#    under each src csproj's bin/Release/.
dotnet pack -c Release shared-libs/ECommerce.Shared.slnx

# 4. Glob-copy all 10 nupkgs into the local feed at the repo root.
cp shared-libs/**/bin/Release/*.<Version>.nupkg local-nuget-packages/

# 5. Verify the feed has the 10 expected files.
ls local-nuget-packages/ECommerce.Shared*<Version>.nupkg
# Expected:
#   ECommerce.Shared.<Version>.nupkg               # umbrella
#   ECommerce.Shared.Kernel.<Version>.nupkg
#   ECommerce.Shared.EventBus.<Version>.nupkg
#   ECommerce.Shared.RabbitMq.<Version>.nupkg
#   ECommerce.Shared.AzureServiceBus.<Version>.nupkg
#   ECommerce.Shared.Messaging.<Version>.nupkg
#   ECommerce.Shared.DeadLetter.<Version>.nupkg
#   ECommerce.Shared.Platform.<Version>.nupkg
#   ECommerce.Shared.Contracts.<Version>.nupkg
#   ECommerce.Shared.Testing.Qa.<Version>.nupkg
```

The umbrella's `.nupkg` carries the nine sub-package `<PackageDependency>`s at the same version (ProjectReference→PackageDependency conversion happens at pack time). Production consumers normally restore only the direct capability packages they reference; restoring against the umbrella alone still pulls the full set transitively for compatibility/prototype consumers.

Older nupkgs with the same version number have been observed to linger in `local-nuget-packages/`. If a consumer build behaves unexpectedly after publish, confirm the nupkg modification time matches your pack run and clear the per-consumer NuGet HTTP cache (`dotnet nuget locals http-cache --clear`) before debugging further.

## Consumer package selection and version sweep

Production consumer csprojs use direct shared-libs capability packages, not the umbrella. A version sweep is one PR per consumer in low-risk-first order. Update every direct `ECommerce.Shared.*` reference in that consumer to the same new lockstep version, and preserve the approved package set unless the service behavior changed.

### Package selection rule

- Auth-only service: `ECommerce.Shared.Platform`, `ECommerce.Shared.Testing.Qa`.
- Publisher/subscriber without shared saga commands: `ECommerce.Shared.Platform`, `ECommerce.Shared.EventBus`, `ECommerce.Shared.Messaging`, `ECommerce.Shared.Testing.Qa`.
- Saga participant or orchestrator using shared commands: `ECommerce.Shared.Platform`, `ECommerce.Shared.EventBus`, `ECommerce.Shared.Messaging`, `ECommerce.Shared.Contracts`, `ECommerce.Shared.Testing.Qa`.
- Gateway/operator DLQ owner: `ECommerce.Shared.Platform`, `ECommerce.Shared.Messaging`, `ECommerce.Shared.DeadLetter`.

`ECommerce.Shared.Messaging` owns `Messaging:Provider` resolution and `AddPlatformEventBus` / `AddPlatformEventPublisher` / `AddPlatformSubscriberService`. Do not add `ECommerce.Shared.DeadLetter`, `ECommerce.Shared.RabbitMq`, or `ECommerce.Shared.AzureServiceBus` to a normal production service solely for provider selection. API Gateway is the only current production consumer with a direct `ECommerce.Shared.DeadLetter` reference.

The executable guardrail is `ConsumerPackageReferenceTests` in [`shared-libs/tests/ECommerce.Shared.LayoutAnalyzer.Tests`](../../shared-libs/tests/ECommerce.Shared.LayoutAnalyzer.Tests/). If a legitimate production package set changes, update that test with the same PR and explain why.

### Sweep order (low-risk first)

| # | Consumer | csproj |
|---|---|---|
| 1 | `auth-microservice` | `auth-microservice/Auth.Service/Auth.Service.csproj` |
| 2 | `basket-microservice` | `basket-microservice/Basket.Service/Basket.Service.csproj` |
| 3 | `product-microservice` | `product-microservice/Product.Service/Product.Service.csproj` |
| 4 | `order-microservice` | `order-microservice/Order.Service/Order.Service.csproj` |
| 5 | `inventory-microservice` | `inventory-microservice/Inventory.Service/Inventory.Service.csproj` |
| 6 | `payment-microservice` | `payment-microservice/Payment.Service/Payment.Service.csproj` |
| 7 | `shipping-microservice` | `shipping-microservice/Shipping.Service/Shipping.Service.csproj` |
| 8 | `saga-microservice` | `saga-microservice/Saga.Service/Saga.Service.csproj` |
| 9 | `api-gateway` | `api-gateway/ApiGateway/ApiGateway.csproj` |

The order surfaces any new-version ABI regression on the least-coupled consumer first. Auth stays first because it exercises Platform without messaging. Basket and Product follow as the smallest subscriber/publisher shapes before the saga participants and Gateway.

### Per-PR loop

```bash
# 1. Edit all direct shared-libs package versions in the consumer csproj.
$EDITOR <consumer-dir>/<Service.Service>.csproj

# 2. Restore + build + test from the consumer directory.
cd <consumer-dir>
dotnet restore
dotnet build
dotnet test
dotnet list package --include-transitive
```

3. For broker-dependent consumers (Basket onward), bring up the service against the live broker and confirm one publish/consume round-trip with outbox + telemetry visually verified:

   ```bash
   docker compose up --build <service>
   ```

4. Run the package guardrail after package-set changes:

   ```bash
   dotnet test shared-libs/tests/ECommerce.Shared.LayoutAnalyzer.Tests/ECommerce.Shared.LayoutAnalyzer.Tests.csproj --filter ConsumerPackageReferenceTests
   ```

### After the sweep

Final commit updates `shared-libs/CLAUDE.md` §"Version pinning history" to record convergence on the new version. Grep gates:

```bash
rg 'PackageReference Include="ECommerce\.Shared"' -g '*.csproj'   # expect zero production service hits
rg 'PackageReference Include="ECommerce\.Shared\.DeadLetter"' -g '*.csproj'   # expect API Gateway only among production services
rg 'PackageReference Include="ECommerce\.Shared\.(RabbitMq|AzureServiceBus)"' -g '*.csproj'   # expect zero production service hits
```

## Adding another capability package

Adding a new shared capability package goes through this checklist; the shape mirrors the existing capability packages (Kernel / EventBus / RabbitMq / AzureServiceBus / Messaging / DeadLetter / Platform / Contracts / Testing.Qa).

1. Scaffold `shared-libs/ECommerce.Shared.<Pkg>/` with `Abstractions/`, `Impl/`, `Composition/` subfolders and one `.placeholder` file per folder so they commit.
2. Create `shared-libs/ECommerce.Shared.<Pkg>/ECommerce.Shared.<Pkg>.csproj`. Inherit version + framework from `Directory.Build.props`. Wire the analyzer as `<ProjectReference Include="..\ECommerce.Shared.LayoutAnalyzer\..." OutputItemType="Analyzer" ReferenceOutputAssembly="false" />`.
3. Add a `<ProjectReference>` line for the new package to `shared-libs/ECommerce.Shared/ECommerce.Shared.csproj` (the umbrella) so it appears in the umbrella's `.nupkg` dependencies.
4. Add the new package + its test csproj to `shared-libs/ECommerce.Shared.slnx`.
5. Add an entry to `CrossPackageAllowlist` in [`shared-libs/ECommerce.Shared.LayoutAnalyzer/LayoutAnalyzer.cs`](../../shared-libs/ECommerce.Shared.LayoutAnalyzer/LayoutAnalyzer.cs) listing the new package's own namespaces plus its allowed upstream dependencies. If the package has nested `Migrations/` or `Models/` namespaces beyond `Abstractions/Impl/Composition/`, also add a `KernelImplNamespaces` / `KernelCompositionNamespaces` entry to keep SHALAY001/SHALAY002 accurate.
6. Scaffold `shared-libs/tests/ECommerce.Shared.<Pkg>.Tests/` with a single `<ProjectReference>` to the new src package and an `Architecture/LayoutTests.cs` mirroring the existing pattern (e.g. [`shared-libs/tests/ECommerce.Shared.Kernel.Tests/Architecture/LayoutTests.cs`](../../shared-libs/tests/ECommerce.Shared.Kernel.Tests/Architecture/LayoutTests.cs)).
7. Add `<InternalsVisibleTo Include="ECommerce.Shared.<Pkg>.Tests" />` to the new src csproj.
8. Bump `<Version>` in `shared-libs/Directory.Build.props` and run the pack-and-publish workflow above. Every capability package plus the umbrella ships together at the new version. The consumer sweep then proceeds in the standard order.

## Out of scope

- Pushing to a non-local NuGet feed (NuGet.org, Azure Artifacts). [ADR-0005](../adr/0005-ecommerce-shared-as-nuget-via-local-feed.md) records the local-feed-only decision; the multi-package split does not change it.
- Adopting `Central Package Management` (`Directory.Packages.props`) across the monorepo. Consumer csprojs continue to declare `<PackageReference … Version="…" />` inline.
- Reverting production consumers to the umbrella package without a new architectural decision. The umbrella remains available, but optimized production consumers should stay narrow-pinned.
