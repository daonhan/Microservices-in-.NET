# Phase 3 — Authorize happy path: implementation summary

> Source plan: [`payment-service.md`](./payment-service.md) — Phase 3 section.
> Source PRD: [`../prd/PRD-Payment.md`](../prd/PRD-Payment.md).
> This file is the as-built starting point for Phase 3 work, derived from a read of the current repo on 2026-04-26.

## Already in place (Phases 1 + 2 shipped)

- `payment-microservice/Payment.Service/` boots end-to-end: SQL Server datastore, outbox, RabbitMQ publisher + subscriber, JWT auth, `Administrator` policy, OTEL, Prometheus, OpenAPI, health probes.
- `payment-microservice/Payment.Service/Models/Payment.cs` — fields + `Create()` factory only. **No state-machine methods yet.**
- `payment-microservice/Payment.Service/Models/PaymentStatus.cs` — `Pending=0, Authorized=1, Captured=2, Refunded=3, Failed=4`.
- `payment-microservice/Payment.Service/Infrastructure/Data/EntityFramework/PaymentContext.cs` + `PaymentConfiguration.cs` — entity + unique index on `OrderId` already configured.
- `payment-microservice/Payment.Service/Migrations/20260425120000_InitialCreate.cs` — table + unique index already shipped.
- `payment-microservice/Payment.Service/Endpoints/PaymentApiEndpoints.cs` — `GET /by-order/{orderId}` and `GET /{paymentId}` already implemented (Phase 6 prework).
- `payment-microservice/Payment.Service/Observability/PaymentMetrics.cs` — `payments_total` counter, `payment_authorize_latency_ms` histogram, `RecordStatusChange(toStatus)`, `RecordAuthorizeLatency(elapsed)`.
- `payment-microservice/Payment.Tests/IntegrationTestBase.cs` mirrors Shipping; only `HealthEndpointTests.cs` exists.
- `inventory-microservice/Inventory.Service/IntegrationEvents/StockReservedEvent.cs` publishes `(OrderId, Items, Amount, Currency)` — Phase 2 contract.
- `order-microservice/Order.Service/IntegrationEvents/Events/StockReservedEvent.cs` is the Order-side consumer record `(OrderId, Amount, Currency)`.
- Routing key = event class name (`RabbitMqEventBus.PublishAsync` line 36). Each service defines its own local copy of an event record; cross-service dispatch matches by name. Payment will define its own copies.

## Critical gap — `StockReservedEvent` carries no `CustomerId`

`PaymentAuthorizedEvent` per PRD is `{ PaymentId, OrderId, CustomerId, Amount, Currency }`. The published `StockReservedEvent` does not carry `CustomerId`.

**Decision: mirror Shipping's pattern.** Payment subscribes to `OrderCreatedEvent` (which carries `CustomerId`) and persists `(OrderId → CustomerId)` in a small table, the same way `Shipping.Service/IntegrationEvents/EventHandlers/StockCommittedEventHandler.cs` (line 28–36) calls `_shipmentStore.TryGetOrderCustomer(orderId)`. Avoids a third contract change to the shared lib.

## Build list

### 1. Aggregate transitions — `Payment.Service/Models/Payment.cs`

Add four methods. PRD requires **throw** on illegal transitions (deep module — not the `bool TryX` pattern from `Shipment`).

- `Authorize(string providerReference, DateTime occurredAt)`: `Pending → Authorized`. Sets `ProviderReference`, `UpdatedAt`. Throws `InvalidOperationException` if not `Pending`.
- `Fail(string reason, DateTime occurredAt)`: `Pending → Failed`. Sets `UpdatedAt`. Throws if not `Pending`. (Reason captured but no field in current entity — extend with `FailureReason string?` column + migration, or carry only on the event. **Carry on event only** — keeps Phase 3 migration-free.)
- `Capture(DateTime occurredAt)`: `Authorized → Captured`. Throws if not `Authorized`.
- `Refund(DateTime occurredAt)`: `Captured → Refunded`. Throws if not `Captured`.

Naming note: plan uses `TryAuthorize / TryFail / TryCapture / TryRefund`. The PRD overrides — methods throw on illegal source, so naming should drop the `Try` prefix to match behavior. Use `Authorize / Fail / Capture / Refund`.

### 2. Payment gateway abstraction

- `Payment.Service/Infrastructure/Gateways/IPaymentGateway.cs`:
  ```csharp
  public interface IPaymentGateway
  {
      Task<PaymentGatewayResult> AuthorizeAsync(decimal amount, string currency, string reference);
      Task<PaymentGatewayResult> CaptureAsync(string reference);
      Task<PaymentGatewayResult> RefundAsync(string reference, decimal amount);
  }

  public record PaymentGatewayResult(bool Success, string? ProviderReference, string? FailureReason);
  ```
- `Payment.Service/Infrastructure/Gateways/InMemoryPaymentGateway.cs`. Deterministic by amount cents. Phase 3 cares about success only:
  - cents `00` → success, `ProviderReference = $"INMEM-{Guid.NewGuid():N}"`.
  - cents `99` → decline (Phase 4 will exercise; encode now so Phase 4 needs no gateway change).
  - All other cents → success (default for happy-path tests).
  - Document mapping in XML doc per PRD.
- DI registration in `Program.cs`: `builder.Services.AddSingleton<IPaymentGateway, InMemoryPaymentGateway>();`.

### 3. Customer-for-order map

- New `Payment.Service/Models/OrderCustomer.cs` — `OrderId (PK Guid), CustomerId (string)`.
- New `Payment.Service/Infrastructure/Data/EntityFramework/OrderCustomerConfiguration.cs` — key on `OrderId`, `CustomerId` required + max length 100.
- `PaymentContext.cs` — add `DbSet<OrderCustomer> OrderCustomers`, apply config.
- `IPaymentStore.cs` — add:
  - `Task RecordOrderCustomer(Guid orderId, string customerId)` — idempotent insert.
  - `Task<string?> TryGetOrderCustomer(Guid orderId)`.
- New EF migration covering `OrderCustomer` table.

### 4. Integration events (Payment-local copies)

- `Payment.Service/IntegrationEvents/Events/OrderCreatedEvent.cs` — `(Guid OrderId, string CustomerId, IReadOnlyList<OrderItem> Items, string Currency = "USD") : Event`. `OrderItem` local record `(string ProductId, int Quantity, decimal UnitPrice = 0m)`. Items unused by Payment but kept so JSON deserializes cleanly.
- `Payment.Service/IntegrationEvents/Events/StockReservedEvent.cs` — `(Guid OrderId, decimal Amount = 0m, string Currency = "USD") : Event`. Drop Items field (Payment doesn't need it).
- `Payment.Service/IntegrationEvents/Events/PaymentAuthorizedEvent.cs` — `(Guid PaymentId, Guid OrderId, string CustomerId, decimal Amount, string Currency) : Event`.

### 5. Event handlers

- `Payment.Service/IntegrationEvents/EventHandlers/OrderCreatedEventHandler.cs` — call `IPaymentStore.RecordOrderCustomer`. Single `TransactionScope`. Idempotent (no-op on duplicate).
- `Payment.Service/IntegrationEvents/EventHandlers/StockReservedEventHandler.cs`:
  1. `customerId = await _store.TryGetOrderCustomer(orderId)`. If null → return (race; redelivery resolves; matches Shipping line 30–36).
  2. `existing = await _store.GetByOrder(orderId)`. If not null → return (idempotency; also backstopped by unique index).
  3. `var sw = Stopwatch.StartNew();` then `var result = await _gateway.AuthorizeAsync(amount, currency, orderId.ToString());` then `_metrics.RecordAuthorizeLatency(sw.Elapsed);`.
  4. If `!result.Success` → throw `InvalidOperationException("Decline path lands in Phase 4")`. Phase 4 will replace with `payment.Fail(...)` + `PaymentFailedEvent`.
  5. On success in single `TransactionScope`:
     - `var payment = Payment.Create(Guid.NewGuid(), orderId, customerId, amount, currency, DateTime.UtcNow);`
     - `payment.Authorize(result.ProviderReference!, DateTime.UtcNow);`
     - Insert via `PaymentContext.Payments.Add` + `SaveChangesAsync`.
     - `_outboxStore.AddOutboxEvent(new PaymentAuthorizedEvent(payment.PaymentId, payment.OrderId, payment.CustomerId, payment.Amount, payment.Currency));`
     - `_metrics.RecordStatusChange(PaymentStatus.Authorized);`

### 6. `Payment.Service/Program.cs` wiring

```csharp
builder.Services.AddSingleton<IPaymentGateway, InMemoryPaymentGateway>();

builder.Services.AddRabbitMqEventBus(builder.Configuration)
    .AddRabbitMqEventPublisher(builder.Configuration)
    .AddRabbitMqSubscriberService(builder.Configuration)
    .AddEventHandler<OrderCreatedEvent, OrderCreatedEventHandler>()
    .AddEventHandler<StockReservedEvent, StockReservedEventHandler>();
```

### 7. Order service — saga edge moves

- New `order-microservice/Order.Service/IntegrationEvents/Events/PaymentAuthorizedEvent.cs` — Order-local consumer copy `(Guid OrderId, string CustomerId, ...)`. Only `OrderId` is required for confirmation; rest tolerated.
- New `order-microservice/Order.Service/IntegrationEvents/EventHandlers/PaymentAuthorizedEventHandler.cs` — body copied from current `StockReservedEventHandler`: load order, `TryConfirm`, emit `OrderConfirmedEvent` via outbox in single `TransactionScope`.
- Edit `order-microservice/Order.Service/IntegrationEvents/EventHandlers/StockReservedEventHandler.cs` — **delete the file**. Saga edge has moved.
- Edit `order-microservice/Order.Service/Program.cs`:
  - Remove `.AddEventHandler<StockReservedEvent, StockReservedEventHandler>()`.
  - Remove unused `using Order.Service.IntegrationEvents.EventHandlers;` if it becomes orphan.
  - Add `.AddEventHandler<PaymentAuthorizedEvent, PaymentAuthorizedEventHandler>()`.
- `order-microservice/Order.Service/IntegrationEvents/Events/StockReservedEvent.cs` — keep the type (tests may still construct it) or delete if no remaining references. Verify with grep before deleting.

### 8. Tests

- `payment-microservice/Payment.Tests/Models/PaymentStateMachineTests.cs` — pure unit, no DB, no DI.
  - Every legal transition succeeds and updates `Status` + `UpdatedAt`.
  - Every illegal source state throws `InvalidOperationException` for each of `Authorize`, `Fail`, `Capture`, `Refund`.
- `payment-microservice/Payment.Tests/IntegrationEvents/CheckoutHappyPathTests.cs` — mirror `shipping-microservice/Shipping.Tests/IntegrationEvents/ShipmentCreationFlowTests.cs`:
  - Dispatch `OrderCreatedEvent(orderId, customerId, items, "USD")` to seed customer.
  - Dispatch `StockReservedEvent(orderId, 50.00m, "USD")`.
  - Assert one `Payment` row, `Status == Authorized`, `Amount == 50.00m`, `CustomerId == "cust-..."`.
  - Assert outbox contains one `PaymentAuthorizedEvent` for that `OrderId`.
  - Use the keyed-handler dispatch helper: `scope.ServiceProvider.GetRequiredKeyedService<IEventHandler>(typeof(TEvent))`.
- `payment-microservice/Payment.Tests/IntegrationEvents/IdempotencyTests.cs` — dispatch the same `StockReservedEvent` twice after seeding customer; assert exactly one row in `Payments`.
- `order-microservice/Order.Tests/IntegrationEvents/PaymentAuthorizedFlowTests.cs` — `PaymentAuthorized → Order.Confirmed + OrderConfirmedEvent on outbox`.
- `order-microservice/Order.Tests` — remove or rewrite any existing `StockReserved → OrderConfirmed` test; that edge no longer exists.

## Phase 3 acceptance mapping

| Plan AC | Where it lands |
| --- | --- |
| Aggregate methods + transition unit tests | Build list §1, §8 |
| `StockReservedEventHandler` creates row, authorizes, transitions, emits via outbox in same tx | §3 + §4 + §5 |
| Order's `PaymentAuthorizedEventHandler` publishes `OrderConfirmedEvent`; old handler removed | §7 |
| Re-delivery does not double-row | §5 step 2 + unique index already in DB |
| `CheckoutHappyPathTests` integration | §8 |
| Order test for `PaymentAuthorized → OrderConfirmed` | §8 |
| Manual smoke `.00` total → Authorized + Confirmed + Shipping created | post-implement verification |

## Risks / decisions to confirm before coding

1. **Customer lookup**: option A (subscribe to `OrderCreatedEvent`, persist mapping). Avoids shared-lib bump beyond Phase 2's locked 1.18.
2. **Aggregate naming**: drop `Try` prefix (`Authorize / Fail / Capture / Refund`) since PRD requires throws on illegal source. The plan's `TryAuthorize` etc. should be amended.
3. **Decline branch in Phase 3 handler**: throw `InvalidOperationException` on `!Success` with explicit "Phase 4 fills this" message; no `Fail()` call yet. Tests use `.00` amount so this branch never executes in Phase 3.
4. **Reason field**: `Payment` has no `FailureReason` column. Carry reason on `PaymentFailedEvent` only — no Phase 3 migration needed for the failure path. Phase 4 may add a column if persisted reason is required.
5. **Order's `StockReservedEvent` record + handler file**: delete handler unconditionally; keep the event record only if any other Order code still references it (verify with grep before deletion).

## Files to create

- `payment-microservice/Payment.Service/Infrastructure/Gateways/IPaymentGateway.cs`
- `payment-microservice/Payment.Service/Infrastructure/Gateways/InMemoryPaymentGateway.cs`
- `payment-microservice/Payment.Service/Models/OrderCustomer.cs`
- `payment-microservice/Payment.Service/Infrastructure/Data/EntityFramework/OrderCustomerConfiguration.cs`
- `payment-microservice/Payment.Service/IntegrationEvents/Events/OrderCreatedEvent.cs`
- `payment-microservice/Payment.Service/IntegrationEvents/Events/StockReservedEvent.cs`
- `payment-microservice/Payment.Service/IntegrationEvents/Events/PaymentAuthorizedEvent.cs`
- `payment-microservice/Payment.Service/IntegrationEvents/EventHandlers/OrderCreatedEventHandler.cs`
- `payment-microservice/Payment.Service/IntegrationEvents/EventHandlers/StockReservedEventHandler.cs`
- `payment-microservice/Payment.Service/Migrations/<timestamp>_AddOrderCustomer.cs`
- `payment-microservice/Payment.Tests/Models/PaymentStateMachineTests.cs`
- `payment-microservice/Payment.Tests/IntegrationEvents/CheckoutHappyPathTests.cs`
- `payment-microservice/Payment.Tests/IntegrationEvents/IdempotencyTests.cs`
- `order-microservice/Order.Service/IntegrationEvents/Events/PaymentAuthorizedEvent.cs`
- `order-microservice/Order.Service/IntegrationEvents/EventHandlers/PaymentAuthorizedEventHandler.cs`
- `order-microservice/Order.Tests/IntegrationEvents/PaymentAuthorizedFlowTests.cs`

## Files to modify

- `payment-microservice/Payment.Service/Models/Payment.cs` — add transition methods.
- `payment-microservice/Payment.Service/Infrastructure/Data/IPaymentStore.cs` — add `RecordOrderCustomer`, `TryGetOrderCustomer`.
- `payment-microservice/Payment.Service/Infrastructure/Data/EntityFramework/PaymentContext.cs` — add `OrderCustomers` DbSet + config.
- `payment-microservice/Payment.Service/Program.cs` — register gateway + two handlers.
- `order-microservice/Order.Service/Program.cs` — swap registration: remove `StockReservedEventHandler`, add `PaymentAuthorizedEventHandler`.
- `docs/plans/payment-service.md` — flip Phase 3 acceptance checkboxes once verified.

## Files to delete

- `order-microservice/Order.Service/IntegrationEvents/EventHandlers/StockReservedEventHandler.cs` — saga edge moved to Payment.
- (Conditional) `order-microservice/Order.Service/IntegrationEvents/Events/StockReservedEvent.cs` — only if no remaining references.
