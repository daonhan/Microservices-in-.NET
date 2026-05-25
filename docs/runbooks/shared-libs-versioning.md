# Shared libraries versioning — bump, publish, sweep

This runbook is the bump-and-publish workflow for `shared-libs/ECommerce.Shared` after [ADR-0013](../adr/0013-shared-libs-multi-package-split.md) split the library into eight capability packages plus one umbrella metapackage. Every shared-libs release ships all nine `.nupkg` files together at a single lockstep version. Every consumer pin sweep is one PR per consumer in low-risk-first order.

## Layout the runbook assumes

- Nine csprojs under [`shared-libs/`](../../shared-libs/) — see ADR-0013 for the partition.
- One `<Version>` in [`shared-libs/Directory.Build.props`](../../shared-libs/Directory.Build.props), inherited by every csproj in the solution.
- One solution at [`shared-libs/ECommerce.Shared.slnx`](../../shared-libs/ECommerce.Shared.slnx) listing every src + test + analyzer csproj.
- Local file-based NuGet feed at [`local-nuget-packages/`](../../local-nuget-packages/) at the repo root (gitignored), referenced by every consumer csproj's `nuget.config` as `../local-nuget-packages`.

## Lockstep semver rule

All nine packages share `<Version>` via `shared-libs/Directory.Build.props`. A release bumps that value once. Sibling-version skew (e.g. `EventBus.3.1.0` + `RabbitMq.3.0.0`) is structurally impossible. The rule is deliberate — eliminating the prior 2.18 / 2.24 / 2.25 consumer-side skew was the motivating decision for the split. If per-package release cadence becomes necessary later, the rule is recorded in ADR-0013 and can be revisited via a follow-up ADR.

The corollary for bump granularity:

- **Patch** — bugfix that touches one or more sub-packages with no public-surface change. Bump all nine.
- **Minor** — additive change (new DI extension, new options field with a default) with no breakage. Bump all nine.
- **Major** — public-surface break (rename, signature change, removed extension). Bump all nine. Roll the consumer sweep procedure below.

## Bump-and-publish workflow

From repo root.

```bash
# 1. Edit the single source-of-truth version.
$EDITOR shared-libs/Directory.Build.props          # bump <Version>3.0.0</Version>

# 2. Build + test the whole solution.
dotnet build shared-libs/ECommerce.Shared.slnx
dotnet test shared-libs/ECommerce.Shared.slnx

# 3. Pack the whole solution. Produces 9 *.<Version>.nupkg
#    under each src csproj's bin/Release/.
dotnet pack -c Release shared-libs/ECommerce.Shared.slnx

# 4. Glob-copy all 9 nupkgs into the local feed at the repo root.
cp shared-libs/**/bin/Release/*.<Version>.nupkg local-nuget-packages/

# 5. Verify the feed has the 9 expected files.
ls local-nuget-packages/ECommerce.Shared*<Version>.nupkg
# Expected (for v3.0.0):
#   ECommerce.Shared.3.0.0.nupkg               # umbrella
#   ECommerce.Shared.Kernel.3.0.0.nupkg
#   ECommerce.Shared.EventBus.3.0.0.nupkg
#   ECommerce.Shared.RabbitMq.3.0.0.nupkg
#   ECommerce.Shared.AzureServiceBus.3.0.0.nupkg
#   ECommerce.Shared.DeadLetter.3.0.0.nupkg
#   ECommerce.Shared.Platform.3.0.0.nupkg
#   ECommerce.Shared.Contracts.3.0.0.nupkg
#   ECommerce.Shared.Testing.Qa.3.0.0.nupkg
```

The umbrella's `.nupkg` carries the eight sub-package `<PackageDependency>`s at the same version (ProjectReference→PackageDependency conversion happens at pack time). Consumer `dotnet restore` against the umbrella alone pulls the full set transitively.

Older nupkgs with the same version number have been observed to linger in `local-nuget-packages/`. If a consumer build behaves unexpectedly after publish, confirm the nupkg modification time matches your pack run and clear the per-consumer NuGet HTTP cache (`dotnet nuget locals http-cache --clear`) before debugging further.

## Consumer version-pin sweep procedure

Every consumer csproj references the **umbrella** by name (`<PackageReference Include="ECommerce.Shared" Version="..." />`). A version-pin sweep is one PR per consumer, in low-risk-first order, each a one-line csproj edit plus the test + smoke gate below.

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

The order surfaces any new-version ABI regression on the least-coupled consumer first. Auth / Basket / Product carry the pre-2.24 latent eager-broker defect and are swept first so the lazy `IRabbitMqConnection` fix lands on the weakest gates first — once a single consumer's integration suite passes against the new version, the broker-startup path is validated for every downstream consumer.

### Per-PR loop

```bash
# 1. Edit the single version line.
$EDITOR <consumer-dir>/<Service.Service>.csproj   # bump <PackageReference Include="ECommerce.Shared" Version="..." />

# 2. Restore + build + test from the consumer directory.
cd <consumer-dir>
dotnet restore
dotnet build
dotnet test
```

3. For broker-dependent consumers (Basket onward), bring up the service against the live broker and confirm one publish/consume round-trip with outbox + telemetry visually verified:

   ```bash
   docker compose up --build <service>
   ```

4. For consumers without a `MessagingProviderBootTests`-equivalent gate (currently Auth, Saga, ApiGateway), add a temporary boot-time assertion in the PR or rely on an integration test to confirm the lazy `IRabbitMqConnection` registration is in place. Once every consumer is on the post-2.24 lazy registration, the gate becomes redundant and can be removed.

### After the sweep

Final commit updates `shared-libs/CLAUDE.md` §"Version pinning history" to record convergence on the new version. Grep gate:

```bash
grep -r 'ECommerce.Shared.*Version="<old-major>' .   # expect zero hits
grep -r 'ECommerce.Shared.*Version="<new-major>' .   # expect 9 hits (one per consumer csproj)
```

## Adding a ninth capability package

Adding a new shared capability package goes through this checklist; the shape mirrors the existing eight (Kernel / EventBus / RabbitMq / AzureServiceBus / DeadLetter / Platform / Contracts / Testing.Qa).

1. Scaffold `shared-libs/ECommerce.Shared.<Pkg>/` with `Abstractions/`, `Impl/`, `Composition/` subfolders and one `.placeholder` file per folder so they commit.
2. Create `shared-libs/ECommerce.Shared.<Pkg>/ECommerce.Shared.<Pkg>.csproj`. Inherit version + framework from `Directory.Build.props`. Wire the analyzer as `<ProjectReference Include="..\ECommerce.Shared.LayoutAnalyzer\..." OutputItemType="Analyzer" ReferenceOutputAssembly="false" />`.
3. Add a `<ProjectReference>` line for the new package to `shared-libs/ECommerce.Shared/ECommerce.Shared.csproj` (the umbrella) so it appears in the umbrella's `.nupkg` dependencies.
4. Add the new package + its test csproj to `shared-libs/ECommerce.Shared.slnx`.
5. Add an entry to `CrossPackageAllowlist` in [`shared-libs/ECommerce.Shared.LayoutAnalyzer/LayoutAnalyzer.cs`](../../shared-libs/ECommerce.Shared.LayoutAnalyzer/LayoutAnalyzer.cs) listing the new package's own namespaces plus its allowed upstream dependencies. If the package has nested `Migrations/` or `Models/` namespaces beyond `Abstractions/Impl/Composition/`, also add a `KernelImplNamespaces` / `KernelCompositionNamespaces` entry to keep SHALAY001/SHALAY002 accurate.
6. Scaffold `shared-libs/tests/ECommerce.Shared.<Pkg>.Tests/` with a single `<ProjectReference>` to the new src package and an `Architecture/LayoutTests.cs` mirroring the existing pattern (e.g. [`shared-libs/tests/ECommerce.Shared.Kernel.Tests/Architecture/LayoutTests.cs`](../../shared-libs/tests/ECommerce.Shared.Kernel.Tests/Architecture/LayoutTests.cs)).
7. Add `<InternalsVisibleTo Include="ECommerce.Shared.<Pkg>.Tests" />` to the new src csproj.
8. Bump `<Version>` in `shared-libs/Directory.Build.props` and run the pack-and-publish workflow above. All nine — now ten — `.nupkg`s ship together at the new version. The consumer sweep then proceeds in the standard order.

## Out of scope

- Pushing to a non-local NuGet feed (NuGet.org, Azure Artifacts). [ADR-0005](../adr/0005-ecommerce-shared-as-nuget-via-local-feed.md) records the local-feed-only decision; the multi-package split does not change it.
- Adopting `Central Package Management` (`Directory.Packages.props`) across the monorepo. Consumer csprojs continue to declare `<PackageReference … Version="…" />` inline.
- Narrow-package consumer pinning. Consumers continue to reference the umbrella by default; opportunistic narrow pinning (e.g. Auth dropping to `ECommerce.Shared.Platform` + `ECommerce.Shared.Testing.Qa` only) is a future PR per consumer.
