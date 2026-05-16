# Plan: Payment Domain-Event Depth + Shared Outbox Unit-of-Work

> Source PRD: [docs/prd/PRD-Payment-Depth-Outbox-UoW.md](../prd/PRD-Payment-Depth-Outbox-UoW.md)
> Baseline after merging `origin/main` on 2026-05-15: incoming `ECommerce.Shared` was `2.14.0`, services use provider-aware messaging registration, RabbitMQ remains the default provider, and Azure Service Bus is selected with `Messaging:Provider=AzureServiceBus`. This PR branch bumps the shared package to `2.15.0` for the Payment Outbox unit-of-work consumer.

## Current implementation status

Already present on the PR branch:

- Shared `IOutboxUnitOfWork` / `OutboxUnitOfWork` registered by `AddOutbox`.
- `ECommerce.Shared` bumped to `2.15.0`, with Payment consuming that version while other services remain on `2.14.0` until they adopt the seam.
- Payment `Entity` / `IDomainEvent` support plus `PaymentCapturedDomainEvent`.
- `PaymentContext.ExecuteAsync` translating captured-payment domain events into existing captured-payment Integration Events.
- Capture endpoint using `paymentStore.ExecuteAsync(...)` instead of direct transaction/outbox ceremony.
- Payment and shared tests covering the first capture-oriented slice, with the Payment test factory disabling the Outbox poller and provider subscriber hosted service to avoid background races.

Still remaining:

- Refund, Authorize, Fail, and Void-style transitions need domain events and context translations where they emit Integration Events.
- Payment event handlers still contain direct `CreateExecutionStrategy + TransactionScope + AddOutboxEvent` ceremony and need migration through the shared seam.
- Inventory and Shipping adoption, unit-of-work observability, package release, and final direct-pattern cleanup are still future phases.

## Architectural decisions

Durable decisions that apply across all phases:

- **Routes**: Payment HTTP routes stay unchanged: `POST /{paymentId:guid}/capture`, `POST /{paymentId:guid}/refund`, and existing internal outbox routes keep their current contracts.
- **Schema**: no new persistent schema is expected. Payment domain events are in-memory, and Outbox tables remain as defined by the existing transactional outbox design.
- **Key models**: `Payment`, Payment domain events, Payment Integration Events, and the shared Outbox unit-of-work module are the load-bearing modules.
- **Authorization**: existing Administrator-only authorization for Capture and Refund stays unchanged.
- **External payment gateway**: payment gateway calls remain outside the database/outbox transaction. The unit-of-work covers only persisted Payment state and Outbox enqueue.
- **Integration Events**: `PaymentAuthorizedEvent`, `PaymentCapturedEvent`, and `PaymentRefundedEvent` payloads remain unchanged.
- **Messaging provider**: the Outbox unit-of-work is broker-agnostic. It writes provider-neutral Outbox rows and leaves delivery to the existing `IEventBus` publisher path selected by `Messaging:Provider`.
- **Saga choreography**: no central orchestrator is introduced. Payment continues to react to and publish Integration Events as a saga participant.
- **Shared package workflow**: `ECommerce.Shared` changes start from the merged `2.14.0` baseline. The Payment slice consumes `2.15.0`; additional consumers still require explicit package upgrades.
- **Testing style**: tests assert external behaviour through the relevant seam. They do not assert private implementation details or raw transaction mechanics.

---

## Phase 1: Capture Through the Deep Seam

**User stories**: 1, 2, 3, 4, 9, 10, 16, 17, 18, 19

### What to build

Build the smallest complete path through the new design. Add the shared Outbox unit-of-work module with enough behaviour to execute persisted work and enqueue Integration Events atomically. Give the Payment aggregate an internal domain-event queue and a captured-payment domain event. Teach the Payment persistence module to translate that domain event into the existing captured-payment Integration Event. Migrate the Capture endpoint so the visible route and response stay unchanged while the endpoint stops managing transaction and outbox ceremony directly.

### Acceptance criteria

- [ ] Capturing an authorized Payment through the existing HTTP route returns the same successful response as before.
- [ ] Capturing an authorized Payment persists the captured state and enqueues exactly one captured-payment Integration Event in the same atomic operation.
- [ ] Capturing a Payment from an illegal state still returns the existing conflict response.
- [ ] Capturing an already captured Payment remains idempotent and does not enqueue a duplicate captured-payment Integration Event.
- [ ] A failure inside the Payment unit-of-work rolls back both the Payment state change and the Outbox enqueue.
- [ ] Payment aggregate tests prove the capture transition and captured-domain-event emission without EF Core, Outbox, RabbitMQ, Azure Service Bus, or the payment gateway.
- [ ] Shared Outbox unit-of-work tests prove atomic commit and rollback behaviour for the initial happy and failure paths.
- [ ] Existing Capture endpoint tests continue to assert HTTP behaviour and resulting Outbox state, not transaction implementation details.

---

## Phase 2: Refund Through the Same Seam

**User stories**: 1, 2, 3, 5, 6, 7, 16, 17, 19

### What to build

Extend the proven Capture path to Refund. Add the refunded-payment domain event, translate it into the existing refunded-payment Integration Event, and migrate the Refund endpoint to use the same Payment persistence seam. Keep the HTTP route, request shape, default full-refund behaviour, and response contract unchanged.

### Acceptance criteria

- [ ] Refunding a captured Payment through the existing HTTP route returns the same successful response as before.
- [ ] Refunding a captured Payment persists the refunded state and enqueues exactly one refunded-payment Integration Event in the same atomic operation.
- [ ] Refunding with an empty body still defaults to the full Payment amount.
- [ ] Refunding a Payment from an illegal state still returns the existing conflict response.
- [ ] A failure inside the refund unit-of-work rolls back both the Payment state change and the Outbox enqueue.
- [ ] Payment aggregate tests prove the refund transition, illegal-state behaviour, and refunded-domain-event emission without infrastructure dependencies.
- [ ] Existing Refund endpoint tests continue to assert HTTP behaviour and resulting Outbox state.

---

## Phase 3: Authorize and Failure Parity

**User stories**: 1, 2, 3, 7, 8, 20

### What to build

Bring the remaining Payment state transitions into the same aggregate/domain-event shape so Payment becomes a complete worked example. Authorize, Fail, and Void-style transitions should follow the same model rules and persistence pattern as Capture and Refund. The phase keeps existing Integration Event contracts stable and aligns idempotency and illegal-transition behaviour with the saga's at-least-once delivery expectations.

### Acceptance criteria

- [ ] Authorizing a pending Payment emits the existing authorized-payment Integration Event through the same context translation path.
- [ ] Failure and void-style transitions follow the aggregate's state-machine rules and do not require endpoint or handler code to construct Integration Events manually.
- [ ] Illegal Payment transitions are enforced by the aggregate regardless of which caller triggers the transition.
- [ ] Idempotent transition behaviour is documented in tests and matches the chosen Order-style semantics.
- [ ] Payment aggregate tests cover every supported transition and every terminal-state rejection.
- [ ] Payment persistence tests verify that each domain event translates to the intended Integration Event without changing event payload contracts.

---

## Phase 4: Adopt the Outbox Unit-of-Work Outside Payment

**User stories**: 9, 10, 11, 12, 13, 18, 20

### What to build

Migrate non-Payment publishers that currently hand-roll execution strategy, transaction scope, and outbox enqueue to the shared Outbox unit-of-work. Start with Inventory reservation handling, then migrate Shipping publishing endpoints. This phase intentionally does not deepen the Shipping aggregate; it only proves the shared Outbox seam has multiple real in-repo adopters.

### Acceptance criteria

- [ ] Inventory reservation handling still reserves stock, handles already-processed orders, emits the same success/failure Integration Events, and records the same reservation metrics.
- [ ] Shipping publishing endpoints still return the same HTTP responses and enqueue the same shipment Integration Events as before.
- [ ] Migrated Inventory and Shipping paths no longer contain direct transaction/outbox ceremony at the call site.
- [ ] Existing Inventory handler tests continue to pass or are rewritten to assert reservation outcomes and resulting Integration Events rather than implementation calls.
- [ ] Existing Shipping endpoint tests continue to pass or are rewritten to assert HTTP outcomes and resulting Integration Events rather than implementation calls.
- [ ] Shared Outbox unit-of-work tests cover event lists that are known before the work and event lists that depend on the result of the work.
- [ ] Provider boot tests continue to prove each migrated service resolves RabbitMQ adapters by default and Azure Service Bus adapters when `Messaging:Provider=AzureServiceBus`.
- [ ] Migrated call sites keep using `AddPlatformEventBus`, `AddPlatformEventPublisher`, and `AddPlatformSubscriberService`; no RabbitMQ-specific publishing dependency is introduced outside the RabbitMQ adapter.

---

## Phase 5: Observability and Package Release

**User stories**: 14, 15, 21, 22, 23

### What to build

Add operational visibility to the new Outbox unit-of-work seam and ship it through the repo's shared-package workflow. The unit-of-work should emit spans and metrics for committed and rolled-back operations. `ECommerce.Shared` should be versioned, packed, published to the local feed, and consumed explicitly by the services migrated in earlier phases.

### Acceptance criteria

- [ ] Successful Outbox unit-of-work executions emit an OTEL span or equivalent telemetry with enough attributes to identify the publishing service and operation outcome.
- [ ] Failed or rolled-back Outbox unit-of-work executions emit telemetry with failure outcome and error context that is safe to log.
- [ ] A shared metric records Outbox transactional success/failure counts across consuming services.
- [ ] Any additional `ECommerce.Shared` release after the `2.15.0` Payment slice is versioned according to the repo's shared-library workflow.
- [ ] A release package is created and published to the local NuGet feed.
- [ ] Payment, Inventory, and Shipping package references are updated to consume the new shared package version.
- [ ] Relevant service test suites and shared-library tests pass after the package upgrade.
- [ ] RabbitMQ-default and Azure Service Bus-selected host-boot tests still pass after the package upgrade.
- [ ] `dotnet format --verify-no-changes --verbosity minimal` passes for the touched solutions or documented service scopes.

---

## Phase 6: Cleanup and Contract Hardening

**User stories**: 13, 19, 22

### What to build

Harden the new seam after adoption. Remove migrated direct transactional publishing patterns from in-repo callers, document the preferred Outbox unit-of-work usage, and make the tests describe stable behaviour rather than low-level implementation details. Keep lower-level Outbox primitives available for the unit-of-work implementation and existing infrastructure code, but make the deep seam the recommended caller-facing path.

### Acceptance criteria

- [ ] Migrated call sites no longer use the direct `CreateExecutionStrategy + TransactionScope + AddOutboxEvent + Complete` pattern.
- [ ] Caller-facing documentation or code comments identify the Outbox unit-of-work as the preferred seam for transactional publishing.
- [ ] `IOutboxStore` remains available for infrastructure implementation needs and unmigrated code, with no breaking public method removals in this plan.
- [ ] Payment endpoint tests assert route-level behaviour and Outbox outcomes, not direct calls to low-level Outbox primitives.
- [ ] Shared Outbox tests cover the public unit-of-work interface as the main test surface.
- [ ] Final code search shows no direct `CreateExecutionStrategy + TransactionScope + AddOutboxEvent + Complete` pattern in migrated Payment, Inventory, or Shipping publishing paths.
- [ ] Payment, Inventory, Shipping, and shared-library builds pass with warnings treated as errors.
- [ ] No new `NoWarn` exemptions are added.
