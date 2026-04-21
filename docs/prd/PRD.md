# PRD: E-Commerce Microservices Platform (.NET 8)

## Problem Statement

Building a monolithic e-commerce application limits independent scaling, deployment agility, and team autonomy. As the system grows, a single codebase becomes harder to maintain, test, and deploy — a change in one area risks breaking another. The system needs an architecture where individual capabilities (product catalog, shopping baskets, ordering, authentication) can evolve, scale, and deploy independently.

## Solution

A microservice-based e-commerce platform using .NET 8 / ASP.NET Core Minimal APIs with event-driven async communication via RabbitMQ, per-service datastores (SQL Server, Redis), shared infrastructure via a NuGet library (`ECommerce.Shared`), observability (OpenTelemetry + Jaeger + Prometheus), resilience (Polly + Outbox pattern), JWT-based security with an API Gateway (Ocelot), and Kubernetes deployment.

## User Stories

### Customer Perspective

1. As a customer, I want to create a shopping basket, so that I can collect products before checkout
2. As a customer, I want to add products to my basket, so that I can purchase multiple items
3. As a customer, I want to remove a product from my basket, so that I can change my mind
4. As a customer, I want to clear my entire basket, so that I can start over
5. As a customer, I want to see my basket total, so that I know how much I'll pay
6. As a customer, I want product prices in my basket to update automatically when prices change, so that I always see accurate totals
7. As a customer, I want to place an order from my basket, so that I can complete a purchase
8. As a customer, I want to view my order details by order ID, so that I can track what I ordered
9. As a customer, I want my basket to clear after ordering, so that I don't accidentally reorder
10. As a customer, I want to browse products by ID, so that I can view product details
11. As a customer, I want to log in with credentials, so that I can access protected features
12. As a customer, I want all API requests to go through a single gateway URL, so that I don't need to know individual service addresses

### Developer / Operator Perspective

13. As a developer, I want each microservice to have its own datastore, so that services are decoupled at the data layer
14. As a developer, I want a shared NuGet library for cross-cutting concerns (RabbitMQ, auth, telemetry), so that I don't duplicate infrastructure code
15. As a developer, I want event-driven communication between services, so that services are temporally decoupled
16. As a developer, I want a transactional outbox for event publishing, so that DB writes and event publication are atomic
17. As a developer, I want distributed tracing across service boundaries, so that I can follow a request end-to-end in Jaeger
18. As a developer, I want custom metrics (order counts, products-per-order histogram), so that I can monitor business KPIs in Prometheus
19. As a developer, I want resilient SQL connections with retry policies, so that transient failures don't crash the service
20. As a developer, I want resilient event publishing with exponential backoff (Polly), so that broker outages are tolerated
21. As a developer, I want JWT auth with role-based access centralized in the API Gateway, so that security is consistent
22. As a developer, I want Docker Compose to spin up the full system locally, so that I can develop and test end-to-end
23. As a developer, I want Kubernetes manifests for every service and dependency, so that I can deploy to a cluster
24. As a developer, I want unit tests with NSubstitute mocks, so that I can test business logic in isolation
25. As a developer, I want integration tests using `WebApplicationFactory`, so that I can test HTTP endpoints with real middleware
26. As a developer, I want RabbitMQ integration tests, so that I can verify event publishing/subscribing end-to-end
27. As a developer, I want each microservice in its own solution with a Dockerfile, so that independent CI/CD is possible
28. As a developer, I want DTOs separating API contracts from internal models, so that internal changes don't break clients
29. As a developer, I want OpenTelemetry context propagation in RabbitMQ headers, so that traces span across async boundaries
30. As a developer, I want keyed DI for generic event handler registration, so that adding new event handlers is a one-liner

## Implementation Decisions

### Architecture

- **5 Microservices:** Basket (port 8000), Order (port 8001), Product (port 8002), Auth (port 8003), API Gateway (port 8004)
- **Framework:** .NET 8, ASP.NET Core Minimal APIs, C# 12
- **Communication:** RabbitMQ fanout exchange (`ecommerce-exchange`), per-service named queues, routing key = event type name
- **Endpoint pattern:** Static `RegisterEndpoints()` extension method on `IEndpointRouteBuilder` per service
- **DI pattern:** Feature-specific extension methods (`AddRabbitMqEventBus()`, `AddOutbox()`, `AddOpenTelemetryTracing()`, `AddJwtAuthentication()`)

### Basket Microservice

- `CustomerBasket` aggregate with `HashSet<BasketProduct>`, computed `BasketTotal = Σ(Quantity × Price)`
- Redis storage via `IBasketStore` → `RedisBasketStore`, `CustomerBasketCacheModel` for serialization
- 24h sliding price cache from `ProductPriceUpdatedEvent`
- Subscribes to `ProductPriceUpdatedEvent` and `OrderCreatedEvent`

### Order Microservice

- `Order` aggregate with `List<OrderProduct>`, quantity aggregation logic
- SQL Server via EF Core, publishes `OrderCreatedEvent` via outbox
- Custom Prometheus metrics: `total-orders` counter, `products-per-order` histogram with buckets [1, 2, 5, 10]

### Product Microservice

- `Product` + `ProductType` models, SQL Server + EF Core Code-First with `HasData()` seeding
- Publishes `ProductPriceUpdatedEvent` via outbox, DTOs (`GetProductResponse`) for API responses
- Eager loading with `.Include(p => p.ProductType)` for navigation properties

### Auth Microservice

- `User` model (Username, Password, Role), SQL Server + EF Core
- `JwtTokenService` with HMAC-SHA256, 15-min token expiry, role claims (`user_role`)
- `POST /login` → `{ token, expiresIn }`

### API Gateway (Ocelot)

- Single entry point with `ocelot.json` route configuration
- Centralized JWT validation, `AuthenticationProviderKey: "Bearer"`
- Role-based route claims: `RouteClaimsRequirement: { "user_role": "Administrator" }` on write endpoints

### Shared Library (`ECommerce.Shared`)

- `Event` base record (Id, CreatedDate), `IEventBus` / `RabbitMqEventBus` publisher
- `IEventHandler<TEvent>` with keyed DI via `AddEventHandler<TEvent, THandler>()`
- `RabbitMqHostedService` subscriber with per-event queue bindings
- Outbox: `OutboxBackgroundService` polls with `PeriodicTimer`, publishes unpublished events, marks as sent
- `IOutboxStore` with `CreateExecutionStrategy()` for transactional scope
- OpenTelemetry: `AddOpenTelemetryTracing()` (auto + manual), `AddOpenTelemetryMetrics()`, `MetricFactory`
- `AuthenticationExtensions.AddJwtAuthentication()` for shared JWT validation
- Distributed as local NuGet packages (v1.1.0 → v1.13.0)

### Resilience

- EF Core `EnableRetryOnFailure(maxRetryCount: 5, maxRetryDelay: 40s)` — built-in exponential backoff
- Polly `ResiliencePipeline` for event publishing — handles `BrokerUnreachableException`, `SocketException`
- Transactional outbox: DB update + outbox insert in single `TransactionScope`, background service publishes async

### Observability

- Auto-instrumentation: ASP.NET Core HTTP spans, SQL Client spans
- Manual instrumentation: `ActivitySource` for RabbitMQ publish/subscribe with `TextMapPropagator` context propagation
- Exporters: Console + OTLP (Jaeger on port 4317), Prometheus scraping `/metrics` (port 9090)

### Deployment

- **Docker Compose:** 10 services (5 microservices + SQL Server, RabbitMQ, Redis, Jaeger, Prometheus)
- **Docker networking:** `host.docker.internal` for container→host communication on Windows
- **Kubernetes:** Per-service Deployment + ClusterIP + LoadBalancer YAML; infrastructure as separate manifests; DNS-based service discovery (`rabbitmq-clusterip-service`, `mssql-clusterip-service`)

## Testing Decisions

- **Good tests** verify external behavior (HTTP responses, events published, DB state), not implementation details
- **Unit tests:** xUnit with `Given_When_Then` naming, Arrange-Act-Assert, NSubstitute for mocking interfaces (`IBasketStore`, `IEventBus`, `IOutboxStore`). `InternalsVisibleTo` for test project access to internal members. Endpoint handler methods extracted from anonymous delegates for testability.
- **Integration tests:** `WebApplicationFactory<Program>` with `appsettings.Tests.json`, override `CreateHost()` for test database and migrations, `IAsyncLifetime` for cleanup (drop test DB). HTTP tests via `HttpClient` + database verification via `DbContext`.
- **RabbitMQ integration tests:** `EventingBasicConsumer` test subscriber captures events, `IDisposable` cleanup deletes test queues/exchanges. End-to-end: trigger API → assert event received.
- **Modules tested:** Basket (domain + endpoints), Order (API + integration), Product (API + integration), RabbitMQ pub/sub
- **Prior art:** `Basket.Tests/`, `Order.Tests/`, `Product.Tests/` projects in `Nhamnhi/` codebase

## Out of Scope

- Payment processing / checkout flow
- User registration / profile management
- Product search / filtering / pagination
- Inventory management
- Notification service (email, SMS)
- Frontend / UI application
- CI/CD pipeline configuration
- Production secrets management (vault, key rotation)
- Load testing / performance benchmarks
- Multi-replica scaling strategies
- Service mesh (Istio / Linkerd)
- Database backup / restore procedures

## Further Notes

- The system is a learning-focused implementation following the Code Maze "Microservices in .NET" course (77 lessons across 10 phases)
- Security key `kR^86SSZu&10RQ1%^k84hii1poPW^CG*` is hardcoded — acceptable for learning, must be externalized for production
- Docker Compose uses `host.docker.internal` for Windows host networking; Kubernetes uses ClusterIP service DNS names
- Shared library versioned via local NuGet packages; production would use a private NuGet feed (Azure Artifacts, GitHub Packages)
- Each microservice has its own `.slnx` solution file for independent development and CI/CD
- Course source code also available in `Microservices-Source-Code/` and converted markdown lessons in `Microservice-MD/`
