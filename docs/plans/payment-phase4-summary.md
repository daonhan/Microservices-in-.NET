# Phase 4 Summary — Failure compensation

> Source plan: [`payment-service.md`](./payment-service.md) §Phase 4 — built on top of Phase 3 commit `13dc8d1`.

## Context

Phase 3 landed authorize happy path. Decline path currently throws `InvalidOperationException` at `payment-microservice/Payment.Service/IntegrationEvents/EventHandlers/StockReservedEventHandler.cs:55`. Phase 4 closes that gap:

- `.99` amount → `Pending → Failed` + `PaymentFailedEvent` from Payment.
- Order's new `PaymentFailedEventHandler` maps that to existing `OrderCancelledEvent`.
- Inventory's existing `OrderCancelledEventHandler` already releases reservations + emits `StockReleasedEvent` — zero changes there.
- Payment also gets an `OrderCancelledEventHandler` to clean up any in-flight `Pending`/`Authorized` row when cancellation arrives from another path.

## What Phase 3 already gave us (reuse)

- **`Payment.Fail(occurredAt)`** — exists, throws unless `Pending`. `payment-microservice/Payment.Service/Models/Payment.cs:51`.
- **`InMemoryPaymentGateway`** — `.99` cents already returns `Success: false` w/ `FailureReason: "Card declined by issuer"`. `payment-microservice/Payment.Service/Infrastructure/Gateways/InMemoryPaymentGateway.cs`.
- **`PaymentMetrics.RecordStatusChange`** — same signature works for `Failed`.
- **Outbox + `TransactionScope` pattern** — Phase 3 `StockReservedEventHandler.Handle` is the template for write+emit.
- **Idempotency primitives** — `IPaymentStore.GetByOrder(orderId)` + unique constraint on `OrderId`.
- **Order's cancel cascade** — `Order.TryCancel()` + `OrderCancelledEvent(Guid OrderId, string CustomerId)` already used by `StockReservationFailedEventHandler` (prior-art template for the new handler).
- **Inventory release path** — `Inventory.OrderCancelledEventHandler` releases reservations + emits `StockReleasedEvent`. Untouched.
- **Test prior art** — `Order.Tests/IntegrationEvents/PaymentAuthorizedFlowTests.cs` shape (DispatchAsync + outbox assertion); `Payment.Tests/IntegrationEvents/CheckoutHappyPathTests.cs`.

## Deltas to land

### 1. New event `PaymentFailedEvent`

Shape per PRD §"New integration events":

```csharp
public record PaymentFailedEvent(
    Guid PaymentId,
    Guid OrderId,
    string CustomerId,
    string Reason) : Event;
```

Locations (mirroring Phase 3's `PaymentAuthorizedEvent` duplication):
- `payment-microservice/Payment.Service/IntegrationEvents/Events/PaymentFailedEvent.cs`
- `order-microservice/Order.Service/IntegrationEvents/Events/PaymentFailedEvent.cs`

### 2. Payment — fail branch in `StockReservedEventHandler`

Replace the throw at the decline path. Restructure so `Payment.Create` happens up front, then branch on `result.Success`:

```csharp
var now = DateTime.UtcNow;
var payment = Models.Payment.Create(
    paymentId: Guid.NewGuid(),
    orderId: @event.OrderId,
    customerId: customerId,
    amount: @event.Amount,
    currency: @event.Currency,
    createdAt: now);

if (!result.Success)
{
    payment.Fail(now);
    _context.Payments.Add(payment);
    await _context.SaveChangesAsync();

    await _outboxStore.AddOutboxEvent(new PaymentFailedEvent(
        payment.PaymentId,
        payment.OrderId,
        payment.CustomerId,
        result.FailureReason ?? "Declined"));

    _metrics.RecordStatusChange(PaymentStatus.Failed);
    scope.Complete();
    return;
}

payment.Authorize(result.ProviderReference!, now);
// rest of happy path unchanged
```

Idempotency guard `existing is not null` (already in handler) covers redelivery for both outcomes.

### 3. Payment — new `OrderCancelledEventHandler`

`payment-microservice/Payment.Service/IntegrationEvents/EventHandlers/OrderCancelledEventHandler.cs`. Single-row aggregate, simpler than Shipping's prior-art:

- `_store.GetByOrder(@event.OrderId)` → if `null` or already terminal (`Failed`/`Captured`/`Refunded`), return.
- If `Pending` → `Fail(now)`.
- If `Authorized` → handled per **Open Question 1** below.
- Save + emit `PaymentFailedEvent` w/ `Reason: "Order cancelled"`, wrapped in outbox `ExecutionStrategy` + `TransactionScope`.

Also add Payment's `OrderCancelledEvent` mirror record under `Payment.Service/IntegrationEvents/Events/`.

### 4. Order — new `PaymentFailedEventHandler`

`order-microservice/Order.Service/IntegrationEvents/EventHandlers/PaymentFailedEventHandler.cs`. Clone of `StockReservationFailedEventHandler`:

```csharp
var order = await _orderStore.GetOrderById(@event.OrderId);
if (order is null || order.Status == OrderStatus.Cancelled) return;
if (!order.TryCancel()) return;
await _orderStore.Commit();
await _outboxStore.AddOutboxEvent(new OrderCancelledEvent(order.OrderId, order.CustomerId));
```

### 5. DI wiring

`payment-microservice/Payment.Service/Program.cs`:
```csharp
.AddEventHandler<OrderCancelledEvent, OrderCancelledEventHandler>()
```

`order-microservice/Order.Service/Program.cs`:
```csharp
.AddEventHandler<PaymentFailedEvent, PaymentFailedEventHandler>()
```

### 6. Tests

- **`Payment.Tests/IntegrationEvents/PaymentFailureCompensationTests.cs`** — mirror `CheckoutHappyPathTests`:
  - `.99` amount → `Failed` row + `PaymentFailedEvent` on outbox.
  - Redelivery of declining `StockReservedEvent` → still single `Failed` row.
- **`Payment.Tests/IntegrationEvents/PaymentOrderCancelledTests.cs`**:
  - `Pending` row → `Failed`.
  - `Authorized` row → terminal (per chosen strategy) + `PaymentFailedEvent`.
  - Already `Captured`/`Refunded`/`Failed` → no-op.
  - No row → no-op (race where cancel arrives before authorize).
- **`Order.Tests/IntegrationEvents/PaymentFailedFlowTests.cs`** — mirror `PaymentAuthorizedFlowTests`:
  - `PaymentFailed` while `PendingStock` → `Cancelled` + `OrderCancelledEvent` on outbox.
  - Already `Cancelled` → no-op.

`PaymentStateMachineTests` already covers `Fail` legality — no changes (unless Open Q1 chooses option (a)).

## Critical files

**Modify**
- `payment-microservice/Payment.Service/IntegrationEvents/EventHandlers/StockReservedEventHandler.cs`
- `payment-microservice/Payment.Service/Program.cs`
- `order-microservice/Order.Service/Program.cs`

**Create**
- `payment-microservice/Payment.Service/IntegrationEvents/EventHandlers/OrderCancelledEventHandler.cs`
- `payment-microservice/Payment.Service/IntegrationEvents/Events/PaymentFailedEvent.cs`
- `payment-microservice/Payment.Service/IntegrationEvents/Events/OrderCancelledEvent.cs`
- `order-microservice/Order.Service/IntegrationEvents/EventHandlers/PaymentFailedEventHandler.cs`
- `order-microservice/Order.Service/IntegrationEvents/Events/PaymentFailedEvent.cs`
- `payment-microservice/Payment.Tests/IntegrationEvents/PaymentFailureCompensationTests.cs`
- `payment-microservice/Payment.Tests/IntegrationEvents/PaymentOrderCancelledTests.cs`
- `order-microservice/Order.Tests/IntegrationEvents/PaymentFailedFlowTests.cs`

## Open questions

1. **Authorized → cancellation transition.** PRD says "voids/refunds and transitions to a terminal state". Aggregate today only allows `Authorized → Captured`. Options:
   - **(a) Add `Payment.Void(occurredAt)`** transitioning `Authorized → Failed`. Cleanest. Recommended.
   - (b) Relax `Refund()` to accept `Authorized`. Conflates semantics.
   - (c) Skip in v1 — only handle `Pending` cancellations. Smallest blast radius; safe because Phase 5 capture not wired yet so the `Authorized` window is brief.

2. **Event emitted for cancellation cleanup.** Reuse `PaymentFailedEvent` w/ `Reason: "Order cancelled"` (PRD events list does not name `PaymentVoided`). Recommended.

3. **Shared-lib version.** Phase 2 bumped `ECommerce.Shared` to 1.18.0. Need to confirm whether `PaymentFailedEvent` was reserved in the 1.18 catalog (no bump) or requires 1.18.1 / 1.19.0. Check `shared-libs/ECommerce.Shared/*.csproj` and Phase 2 commit `853e990` before publishing.

## Verification

- `dotnet build` solution-wide green.
- `dotnet test payment-microservice/Payment.Tests` green incl. `PaymentFailureCompensationTests` + `PaymentOrderCancelledTests`.
- `dotnet test order-microservice/Order.Tests` green incl. `PaymentFailedFlowTests`.
- Manual smoke (per plan §Phase 4):
  - `docker compose up --build`.
  - Place an order whose total ends `.99`.
  - `GET /payment/by-order/{orderId}` → `Failed`.
  - `GET /order/{customerId}/{orderId}` → `Cancelled`.
  - `GET /inventory/{productId}` → reserved count back to zero.
  - RabbitMQ management UI → `PaymentFailedEvent`, `OrderCancelledEvent`, `StockReleasedEvent` published in order.
  - Jaeger spans Payment → Order → Inventory cleanly.
