# Auth — service notes

Clean Architecture + Vertical Slices is the default service shape ([ADR-0012](../docs/adr/0012-clean-arch-vsa-default-service-shape.md)): `Features/<Slice>/`, `Domain/`, `Infrastructure/` (no `Contracts/`).

Boundaries enforced by NetArchTest (`Auth.Tests/Architecture/LayoutTests.cs`) and the Roslyn `Auth.Service.LayoutAnalyzer`.

Composes ADR [0011](../docs/adr/0011-order-cleanarch-vsa-pilot.md) by reference (original pilot); reuses [adding-a-new-slice.md](../docs/runbooks/adding-a-new-slice.md) runbook unchanged.

Narrow-pins shared-libs per [ADR-0013](../docs/adr/0013-shared-libs-multi-package-split.md) and [shared-libs-versioning.md](../docs/runbooks/shared-libs-versioning.md): `ECommerce.Shared.Platform` and `ECommerce.Shared.Testing.Qa`.

## Divergences from Order/Product

- **No `Contracts/` folder** — Auth produces and consumes no cross-service payloads.
- **No outbox seam, no integration events.**

## Cross-cutting

- RS256 user JWTs (`POST /login`); `client_credentials` service tokens (`POST /token`).
- Dev keys: `Auth.Service/dev-keys/`.
- Resources elsewhere validate via `AddJwtAuthentication()` (fetches+caches `/jwks` — no shared secret).
- `AuthQaOperatorSeeder` seeds the break-glass `operator@qa.test` (`Role=Operator`) for the DLQ smoke suite via the existing `SeedQaData(...)` call, gated by `IsQaSeedingEnabled` (Development OR `Qa:Seed`). Env-gated runtime seeder, not a migration — [ADR-0014](../docs/adr/0014-env-gated-qa-runtime-seeders-for-operator-and-dlq.md). **Never add `Qa__Seed` to a non-dev manifest** — it would insert this high-privilege account there.
