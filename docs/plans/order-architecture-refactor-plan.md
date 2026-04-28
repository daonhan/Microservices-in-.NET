# Plan: Order Architecture Refactor

> Source PRD: docs/prd/order-architecture-refactor.md

## Architectural decisions

Durable decisions that apply across all phases:

- **Routes**: The API routes `/{customerId}` and `/{customerId}/{orderId}` will remain unchanged.
- **Schema**: The underlying database schema for `Order` and `OrderProduct` will remain unchanged.
- **Key models**: The `Order` model will be enhanced with domain event capabilities, but its core data properties remain identical.
- **Interfaces**: A new `IProductPriceProvider` interface will be introduced as the seam for product pricing.

---

## Phase 1: Product Pricing Adapter Extraction

**User stories**: 3, 4, 5

### What to build

Extract the logic for fetching product prices from `IDistributedCache` in the order creation endpoint. Create a new `IProductPriceProvider` interface and a `RedisProductPriceProvider` implementation. Inject the interface into the endpoint and use it to retrieve product prices.

### Acceptance criteria

- [x] `IProductPriceProvider` interface is defined.
- [x] `RedisProductPriceProvider` is implemented and registered in DI.
- [x] `OrderApiEndpoint.CreateOrder` uses `IProductPriceProvider`.
- [x] Unit tests for `OrderApiEndpoint.CreateOrder` use a mocked `IProductPriceProvider`.
- [x] Integration test for `RedisProductPriceProvider` validates Redis caching behavior.

---

## Phase 2: Domain Events & Handler Unit of Work Refactoring

**User stories**: 1, 2, 6

### What to build

Introduce a Unit of Work boundary that automatically translates domain events on the `Order` aggregate into outbox messages within a `TransactionScope`. Modify the `Order` aggregate to queue domain events when its state changes. Refactor `PaymentAuthorizedEventHandler` and `StockReservationFailedEventHandler` to remove their manual transaction and outbox boilerplate, relying entirely on the new Unit of Work during `Commit()`.

### Acceptance criteria

- [ ] `Order` aggregate holds a collection of `IDomainEvent`s.
- [ ] `Order.TryConfirm` and `Order.TryCancel` queue appropriate domain events.
- [ ] `IOrderStore.Commit()` (or equivalent Unit of Work pipeline) executes within an EF execution strategy and `TransactionScope`, and dispatches queued domain events to the outbox.
- [ ] `PaymentAuthorizedEventHandler` and `StockReservationFailedEventHandler` are refactored to remove explicit `TransactionScope` and outbox translation.
- [ ] Handlers are unit tested with a mocked `IOrderStore`.

---

## Phase 3: Endpoint Unit of Work Refactoring

**User stories**: 1, 2, 6

### What to build

Apply the new Unit of Work pattern to the `OrderApiEndpoint.CreateOrder` endpoint. Ensure the `Order` aggregate queues an `OrderCreatedEvent` upon creation, and the endpoint relies on `IOrderStore.Commit()` to handle the transaction and outbox insertion, completely decoupling the endpoint from outbox and EF transaction specifics.

### Acceptance criteria

- [ ] `Order` aggregate queues the `OrderCreatedEvent` upon valid creation.
- [ ] `OrderApiEndpoint.CreateOrder` is refactored to remove explicit `TransactionScope` and outbox translation.
- [ ] Tests verify that creating an order successfully persists the order and the outbox event in the database using the new Unit of Work.
