# Order Service Architecture Refactor PRD

## Problem Statement
The `Order.Service` codebase suffers from architectural friction due to shallow modules and leaky abstractions. Specifically, transaction management and outbox message dispatching leak into every use-case handler, resulting in duplicated boilerplate and a lack of locality. Furthermore, the order creation API endpoint directly interacts with the Redis cache to fetch product prices, tightly coupling the endpoint to physical caching formats and external domain concepts.

## Solution
Deepen the architecture by extracting two highly cohesive modules to act as new seams:
1. A Unit of Work / Domain Event Dispatcher seam that encapsulates `TransactionScope`, EF Core execution strategies, and Outbox event creation.
2. A Product Pricing adapter seam that encapsulates the `IDistributedCache` interaction, cache key generation, and string parsing.

## User Stories
1. As a developer, I want to create new order-related use cases without writing transaction and outbox boilerplate, so that I can focus purely on domain logic.
2. As a developer, I want all outbox events to be automatically dispatched when an aggregate's state changes, so that I never forget to emit an event manually.
3. As a developer, I want to fetch product prices through a strongly-typed domain interface, so that I do not need to know the underlying Redis cache structure or parsing logic.
4. As a developer, I want to test the order intake endpoint without needing a real Redis instance, so that my unit tests are fast and reliable.
5. As a maintainer, I want all changes to product price caching to be isolated in one adapter, so that I don't accidentally break the order creation process.
6. As a maintainer, I want the transaction scope and execution strategy logic in exactly one place, so that any future changes to persistence are highly localized.

## Implementation Decisions
- **Transaction/Outbox Orchestrator Module**:
  - Introduce an `Entity` base class or similar mechanism on `Order` to hold a list of `IDomainEvent`s.
  - Modify `IOrderStore.Commit()` (or introduce a new orchestrator pipeline/decorator) to automatically wrap the EF execution strategy, create a `TransactionScope`, save the `DbContext`, and translate internal domain events into the `IOutboxStore`.
  - Refactor `OrderApiEndpoint.cs`, `PaymentAuthorizedEventHandler.cs`, and `StockReservationFailedEventHandler.cs` to remove explicit transaction scopes and outbox calls. They will rely entirely on the new seam.
- **Product Price Adapter Module**:
  - Create a new interface `IProductPriceProvider` with a method like `Task<Dictionary<string, decimal>> GetUnitPricesAsync(IEnumerable<string> productIds)`.
  - Create an implementation `RedisProductPriceProvider` that injects `IDistributedCache` and handles the string-to-decimal parsing and missing key validation.
  - Update `OrderApiEndpoint` to consume `IProductPriceProvider` instead of `IDistributedCache`.

## Testing Decisions
- **Test Philosophy**: A good test should verify the external behavior of the module without knowing its implementation details. Testing against the interface validates the seam.
- **Modules to be tested**:
  - The `OrderApiEndpoint` logic will be unit tested by mocking `IProductPriceProvider` and `IOrderStore`, ensuring that the correct domain logic is executed.
  - The `RedisProductPriceProvider` will be tested via integration tests using a real or containerized Redis instance to ensure it properly deserializes values.
  - Handlers (`PaymentAuthorizedEventHandler`, `StockReservationFailedEventHandler`) will be unit tested using mocked persistence seams, checking that the correct domain methods (`TryConfirm`, `TryCancel`) are invoked on the aggregate.

## Out of Scope
- Modifying other microservices (like `auth-microservice`, `basket-microservice`, etc.).
- Refactoring or altering the core implementation of `ECommerce.Shared.Infrastructure.Outbox`.
- Changing the existing `Order` and `OrderProduct` database schema.

## Further Notes
- This refactoring does not change external API contracts or message contracts.
- By introducing these deep modules, the codebase will be significantly more AI-navigable and testable.
