# Auth — service notes

Clean Architecture + Vertical Slices: `Features/<Slice>/`, `Domain/`, `Infrastructure/` (no `Contracts/`).

Boundaries enforced by NetArchTest (`Auth.Tests/Architecture/LayoutTests.cs`) and the Roslyn `Auth.Service.LayoutAnalyzer`.

Composes ADR [0011](../docs/adr/0011-order-cleanarch-vsa-pilot.md) by reference (no new ADR); reuses [adding-a-new-slice.md](../docs/runbooks/adding-a-new-slice.md) runbook unchanged.

## Divergences from Order/Product

- **No `Contracts/` folder** — Auth produces and consumes no cross-service payloads.
- **No outbox seam, no integration events.**

## Cross-cutting

- RS256 user JWTs (`POST /login`); `client_credentials` service tokens (`POST /token`).
- Dev keys: `Auth.Service/dev-keys/`.
- Resources elsewhere validate via `AddJwtAuthentication()` (fetches+caches `/jwks` — no shared secret).
