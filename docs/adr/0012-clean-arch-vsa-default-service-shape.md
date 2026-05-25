# ADR-0012 — Clean Architecture + Vertical Slices is the default service shape

- **Status**: Accepted
- **Date**: 2026-05-25

## Context

ADR-0011 reorganized `Order.Service` onto Clean Architecture + Vertical Slices and explicitly scoped the change as a single-service pilot, deferring propagation to "a separate ADR informed by pilot learnings". Between 2026-05-22 and 2026-05-25 the convention propagated to eight more services as composing pilots (`Product`, `Basket`, `Auth`, `Inventory`, `Shipping`, `Payment`, `Saga`, `ApiGateway`). With api-gateway closing out the migration, every service in the monorepo is on the layout.

The propagation was tracked in root [`CLAUDE.md`](../../CLAUDE.md) as a stack of per-service "exception" paragraphs (one per pilot), each ending with a "Propagation to remaining services is a separate ADR" footer. The exception list grew with every pilot and the framing — VSA as a series of one-off exceptions — no longer matches reality.

PRD: [PRD-ApiGateway-CleanArch-VSA-Pilot.md](../prd/PRD-ApiGateway-CleanArch-VSA-Pilot.md).

## Decision

Promote Clean Architecture + Vertical Slices from "per-service pilot exception" to the **default service shape** for the monorepo.

- New services join the monorepo on the default shape: `Features/<Slice>/`, `Domain/`, `Contracts/Integration/`, `Infrastructure/`. Boundaries enforced per service by NetArchTest (`<Svc>.Tests/Architecture/LayoutTests.cs`) and a Roslyn `<Svc>.Service.LayoutAnalyzer`. New slices follow the [adding-a-new-slice.md](../runbooks/adding-a-new-slice.md) runbook.
- ADR-0011 remains in force as the original-pilot decision record (Order). Only the propagation guidance in its "Pilot scope" and "Follow-ups" sections — the deferred-to-separate-ADR clauses — is superseded by this ADR.
- The "Propagation to remaining services is a separate ADR" footer present on the seven composing-pilot exception blocks in root `CLAUDE.md` is rendered historical by this ADR and removed during the same sweep.
- Deviations from the default shape are permitted only when justified by a service's nature (no aggregate, no integration events, multi-producer flow, multi-aggregate hosting). Permitted divergences are listed below; further deviations require an amendment to this ADR.

### Permitted divergences (recorded, not erased)

- **Auth** — no `Contracts/` folder. Auth produces and consumes no cross-service integration events; the `Contracts/Integration/` layer carries no payload, so the folder is omitted rather than created empty.
- **ApiGateway** — no `Contracts/` and no `Domain/` folders. Gateway owns no aggregate (the DLQ entity `DeadLetterMessage` lives in `ECommerce.Shared.Infrastructure.DeadLetter`) and publishes no integration events; both layers are omitted. Gateway is the only service on the layout without a `Domain/`.
- **Basket / Inventory / Shipping / Saga** — no `IIntegrationMap<,>` + `DomainEventOutboxInterceptor` outbox seam. The translation seam exists to dissolve a `Translate(...)` smell in the producing service's `DbContext`. Basket emits no integration events, and Inventory/Shipping/Saga construct integration events or saga commands inline per slice — there is no central `Translate` switch to extract.
- **Payment** — multi-producer slice convention. The HTTP write slice (e.g. `Features/CapturePayment/`) and the saga command slice (e.g. `Features/CapturePaymentCommand/`) raise the same domain event from a shared `Payment` aggregate, and the `IIntegrationMap<,>` is resolved globally by `DomainEventOutboxInterceptor` via DI. The HTTP slice owns the mapper file; the saga slice does not reference it as source — both slices independently call domain methods, and the interceptor handles translation. Not a slice-to-slice source reference.
- **Saga** — two-level `Features/<Saga>/<Trigger>/` namespace nesting (two saga aggregates, `OrderSaga` and `RefundSaga`, coexist in one service) and a dual-subscription convention for `PaymentRefundedEvent`. `Features/OrderSaga/PaymentRefunded/` and `Features/RefundSaga/PaymentRefunded/` both register handlers for the event; each loads its own saga by id and no-ops if the id is not its own. Only place in the monorepo where one integration event drives two slices that must both act on it.

## Consequences

- Onboarding doc for new services and new slices is the [adding-a-new-slice.md](../runbooks/adding-a-new-slice.md) runbook, not a growing exception list in `CLAUDE.md`.
- Root `CLAUDE.md`'s `## Service layout` section collapses the prior eight per-service exception paragraphs into one default-shape paragraph plus a short divergence list. The section now shrinks as services normalize toward the default, rather than growing per pilot.
- The two-layer boundary enforcement (NetArchTest + Roslyn analyzer) becomes the standard, not the exception. New services are expected to ship both.
- Adding a new service without `Features/<Slice>/`, `Domain/`, `Contracts/Integration/`, `Infrastructure/`, NetArchTest layout tests, and a Roslyn layout analyzer requires an ADR amendment listing the deviation. Without an amendment the deviation is a defect.
- `ECommerce.Shared` remains the seam for cross-cutting concerns; the divergences here are about per-service folder shape, not shared-library composition.
- This ADR records divergences; it does not remove them. Erasing a recorded divergence (e.g. introducing an outbox seam in Saga, collapsing Saga's two-level nesting, or moving `JwtClaimTypes` from `ApiGateway/Infrastructure/Auth/` to `ECommerce.Shared`) is a separate decision tracked in its own ADR.

## Supersedes / Composes

- **Supersedes the propagation guidance of [ADR-0011](0011-order-cleanarch-vsa-pilot.md)** — specifically the "Pilot scope" clause that scopes the layout to `Order.Service` only and the "Follow-ups → Propagation to other services" clause that defers propagation to a separate ADR. ADR-0011 itself remains in force as the Order-pilot decision record; only its forward-looking propagation guidance is superseded.
- **Composes [ADR-0011](0011-order-cleanarch-vsa-pilot.md) by reference.** All non-propagation decisions in ADR-0011 — single csproj per service, folder topology, direct DI dispatch, domain richness rule, outbox translation seam, cross-slice rule, boundary enforcement, namespace conventions, composition root as manifest — apply unchanged as the default shape.
