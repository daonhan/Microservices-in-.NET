# Plan: E-Commerce Microservices Platform

> Source PRD: `prd/PRD.md`

## Architectural Decisions

Durable decisions that apply across all phases:

- **Framework**: .NET 8, ASP.NET Core Minimal APIs, C# 12
- **Endpoint pattern**: Static `RegisterEndpoints()` extension method on `IEndpointRouteBuilder` per microservice
- **DI pattern**: Feature-specific extension methods on `IServiceCollection` (e.g., `AddRabbitMqEventBus()`, `AddOutbox()`, `AddJwtAuthentication()`)
- **Service ports**: Basket (8000), Order (8001), Product (8002), Auth (8003), API Gateway (8004)
- **Messaging**: RabbitMQ fanout exchange (`ecommerce-exchange`), per-service named queues, routing key = event type name
- **Event base**: `Event` base record with `Id` (Guid) and `CreatedDate` (DateTime)
- **Data ownership**: Each microservice owns its datastore — Basket (Redis), Order (SQL Server), Product (SQL Server), Auth (SQL Server)
- **Schema shape**: Product (`Id`, `Name`, `Description`, `Price`, `ProductTypeId`), Order (`OrderId`, `CustomerId`, `OrderDate` + `OrderProduct` collection), CustomerBasket (`CustomerId` + `BasketProduct` set), User (`Username`, `Password`, `Role`)
- **Auth approach**: JWT Bearer tokens with HMAC-SHA256 symmetric key, 15-min expiry, `user_role` claim for RBAC
- **Docker networking**: `host.docker.internal` for local container→host; Kubernetes uses ClusterIP service DNS names
- **Shared library**: `ECommerce.Shared` NuGet package for cross-cutting concerns (messaging, auth, observability, outbox)

---

## Phase 1: Basket CRUD with In-Memory Store

**User stories**: 1, 2, 3, 4, 13

### What to build

A standalone Basket microservice that supports creating a customer basket, adding products, removing products, and clearing a basket. The `CustomerBasket` aggregate root encapsulates a `HashSet<BasketProduct>` with domain methods. Storage is behind an `IBasketStore` interface with an `InMemoryBasketStore` implementation, registered via DI so it can be swapped later. The service runs in a Docker container on port 8000 with a multi-stage Dockerfile (base → build → publish → final).

### Acceptance criteria

- [ ] `POST /{customerId}` creates a new basket with an initial product and returns the basket
- [ ] `PUT /{customerId}` adds a product to an existing basket (updates quantity if product already exists)
- [ ] `DELETE /{customerId}/{productId}` removes a specific product from the basket
- [ ] `DELETE /{customerId}` clears the entire basket
- [ ] `GET /{customerId}` retrieves the basket with all products
- [ ] `CustomerBasket` enforces domain invariants (no duplicate products, quantity management)
- [ ] Service runs in Docker container with `docker run -p 8000:8080`
- [ ] `IBasketStore` interface allows swapping storage implementation without changing endpoint code

---

## Phase 2: Order Creation & Sync Event Publishing

**User stories**: 7, 8, 15

### What to build

An Order microservice on port 8001 that accepts order creation requests and retrieves orders by ID. The `Order` aggregate root manages a list of `OrderProduct` items with quantity aggregation logic. After successfully creating an order, the service publishes an `OrderCreatedEvent` to RabbitMQ using a direct `IEventBus` → `RabbitMqEventBus` implementation. Docker Compose orchestrates both Basket and Order microservices alongside RabbitMQ. This phase proves end-to-end event publishing through a message broker.

### Acceptance criteria

- [ ] `POST /{customerId}` creates an order with products and returns order details
- [ ] `GET /{customerId}/{orderId}` retrieves order by ID
- [ ] `Order` aggregate aggregates quantities for duplicate product IDs
- [ ] `OrderCreatedEvent` is published to RabbitMQ fanout exchange on successful order creation
- [ ] RabbitMQ management UI (port 15672) shows the event in the exchange
- [ ] Docker Compose runs Order, Basket, and RabbitMQ together
- [ ] `RabbitMqEventBus` uses singleton connection (network-intensive) and scoped channels

---

## Phase 3: Event Subscription & Basket Clearing

**User stories**: 9, 15

### What to build

The Basket microservice subscribes to `OrderCreatedEvent` from RabbitMQ. A `RabbitMqHostedService` (implementing `IHostedService`) runs as a background service, consuming messages from a Basket-specific queue bound to the fanout exchange. When an `OrderCreatedEvent` is received, the `OrderCreatedEventHandler` clears the customer's basket. This phase proves bidirectional async communication between two microservices through RabbitMQ.

### Acceptance criteria

- [ ] Basket microservice starts a hosted service that connects to RabbitMQ and listens on its own queue
- [ ] Queue is bound to the `ecommerce-exchange` fanout exchange
- [ ] When Order service publishes `OrderCreatedEvent`, Basket service receives it and clears the customer's basket
- [ ] End-to-end test: create basket → create order → verify basket is cleared
- [ ] Background consumer uses `EventingBasicConsumer` with `Task.Factory.StartNew()` for long-running thread

---

## Phase 4: Shared Library Extraction & Distribution

**User stories**: 14, 30

### What to build

Extract duplicated RabbitMQ infrastructure code into a shared `ECommerce.Shared` NuGet library. The library contains the `Event` base record, `IEventBus`, `RabbitMqEventBus`, `RabbitMqConnection`, `RabbitMqOptions`, and startup extension methods. Add generic event handling with `IEventHandler<TEvent>` interface and `EventHandlerRegistration` using keyed DI (`AddKeyedTransient`). The library is packaged via `dotnet pack` and distributed as a local NuGet package. Both Order and Basket microservices consume via `nuget.config` pointing to the local package folder. The rewritten `RabbitMqHostedService` loops registered events, binds queues, and dispatches to the correct handler.

### Acceptance criteria

- [ ] `ECommerce.Shared` library builds and packs as a NuGet package
- [ ] `AddEventHandler<TEvent, THandler>()` registers handlers via keyed DI — adding a new handler is a one-liner
- [ ] `RabbitMqStartupExtensions` provides `AddRabbitMqEventBus()`, `AddRabbitMqEventPublisher()`, `AddRabbitMqSubscriberService()`
- [ ] Both Order and Basket microservices consume the shared library via `nuget.config`
- [ ] Existing event publishing and subscribing behavior is preserved after migration to shared library
- [ ] `RabbitMqHostedService` dynamically binds queues and dispatches events based on registered handlers

---

## Phase 5: Product Microservice with SQL Persistence

**User stories**: 10, 13, 28

### What to build

A Product microservice on port 8002 with SQL Server persistence via EF Core Code-First. The `Product` model has a `ProductType` navigation property. Database schema is defined via `IEntityTypeConfiguration<T>` fluent configurations with seed data via `HasData()`. A `MigrateDatabase()` call at startup applies migrations in development. API responses use `GetProductResponse` DTOs to decouple internal models from API contracts. Docker Compose adds a SQL Server container.

### Acceptance criteria

- [ ] `GET /{productId}` returns product details with product type via DTO
- [ ] `POST /` creates a new product (protected endpoint)
- [ ] `PUT /{productId}` updates product details (protected endpoint)
- [ ] EF Core migrations create the schema; `HasData()` seeds product types
- [ ] `MigrateDatabase()` runs at startup in Development environment
- [ ] Eager loading via `.Include(p => p.ProductType)` for navigation properties
- [ ] SQL Server runs in Docker Compose alongside other services
- [ ] DTOs prevent leaking internal model structure to API consumers

---

## Phase 6: Event-Driven Price Sync & Redis Cache

**User stories**: 5, 6, 16

### What to build

When a product's price is updated, the Product microservice publishes a `ProductPriceUpdatedEvent`. The Basket microservice subscribes and caches the new price in Redis via `IDistributedCache` with a 24-hour sliding expiration. When retrieving a basket, the service enriches each `BasketProduct` with the cached price and computes `BasketTotal = Σ(Quantity × Price)`. Docker Compose adds a Redis container.

### Acceptance criteria

- [ ] Product `PUT` endpoint publishes `ProductPriceUpdatedEvent` when price changes
- [ ] Basket microservice subscribes to `ProductPriceUpdatedEvent` and caches the new price in Redis
- [ ] Price cache entries have 24-hour sliding expiration
- [ ] `GET /{customerId}` enriches basket products with cached prices
- [ ] `BasketTotal` is correctly computed from quantity × cached price for all products
- [ ] Redis container runs in Docker Compose on port 6379

---

## Phase 7: Basket Persistence in Redis

**User stories**: 1, 2, 3, 4 (hardened)

### What to build

Replace `InMemoryBasketStore` with `RedisBasketStore` implementing the same `IBasketStore` interface. Introduce `CustomerBasketCacheModel` as a serialization-friendly record separate from the domain model, handling the impedance mismatch between Redis JSON storage and the domain's `HashSet`. Convert all basket endpoints to `async`. Baskets now survive service restarts.

### Acceptance criteria

- [ ] `RedisBasketStore` implements `IBasketStore` using `IDistributedCache`
- [ ] `CustomerBasketCacheModel` maps to/from `CustomerBasket` domain model
- [ ] All basket endpoints are async
- [ ] Baskets persist across service restarts (verified by restarting the container)
- [ ] `InMemoryBasketStore` can still be used by swapping DI registration (no code changes to endpoints)

---

## Phase 8: Transactional Outbox Pattern

**User stories**: 16

### What to build

Replace direct event publishing with a transactional outbox. When a service writes to its database, it also inserts an outbox event record within the same `TransactionScope`. An `OutboxBackgroundService` (inheriting `BackgroundService`) polls with a `PeriodicTimer`, retrieves unpublished events from the `IOutboxStore`, publishes them via `IEventBus`, and marks them as sent. This goes into the shared library. Both Product and Order microservices adopt the outbox for all event publishing.

### Acceptance criteria

- [ ] Outbox table stores event type, serialized data, and published status
- [ ] DB write + outbox insert happen in a single `TransactionScope` — atomic commit
- [ ] `OutboxBackgroundService` polls periodically and publishes unpublished events
- [ ] Events are marked as published after successful `IEventBus.PublishAsync()`
- [ ] If the service crashes after DB commit but before publishing, the outbox ensures eventual delivery
- [ ] Product and Order microservices both use the outbox (no direct `IEventBus.PublishAsync()` from endpoints)
- [ ] EF Core migrations for the outbox table run at startup

---

## Phase 9: Unit & Integration Testing

**User stories**: 24, 25, 26

### What to build

Add test projects for Basket, Order, and Product microservices. Unit tests use xUnit with `Given_When_Then` naming and NSubstitute for mocking interfaces. Endpoint handler methods are extracted from anonymous delegates into named static methods for testability. Integration tests use `WebApplicationFactory<Program>` with test-specific configuration (`appsettings.Tests.json`), overriding `CreateHost()` for test databases. RabbitMQ integration tests use `EventingBasicConsumer` to capture published events. `IAsyncLifetime` handles cleanup (drop test DB, delete test queues/exchanges).

### Acceptance criteria

- [ ] Basket unit tests cover domain logic (`CustomerBasket` add/remove/clear) and endpoint handlers
- [ ] Order integration tests verify `POST` creates order in DB and `GET` returns correct data
- [ ] Product integration tests verify CRUD operations against a test database
- [ ] RabbitMQ integration tests verify event publishing by consuming from a test queue
- [ ] `InternalsVisibleTo` allows test projects to access internal members
- [ ] Test databases are created and dropped per test class via `IAsyncLifetime`
- [ ] All tests pass in CI-compatible isolation (no shared state between test runs)

---

## Phase 10: Observability — Tracing & Metrics

**User stories**: 17, 18, 29

### What to build

Add OpenTelemetry instrumentation across all microservices. Auto-instrumentation covers ASP.NET Core HTTP spans and SQL Client spans. Manual instrumentation adds `ActivitySource`-based spans for RabbitMQ publish and subscribe operations with semantic conventions (`messaging.system`, `messaging.operation.name`). Context propagation via `TextMapPropagator` injects/extracts `traceparent` headers across RabbitMQ message boundaries. Traces export to Jaeger via OTLP (port 4317). Custom metrics include a `total-orders` counter and `products-per-order` histogram via `MetricFactory`, exported to Prometheus. Docker Compose adds Jaeger and Prometheus containers.

### Acceptance criteria

- [ ] HTTP requests generate spans visible in Jaeger UI (port 16686)
- [ ] SQL queries appear as child spans of HTTP request spans
- [ ] RabbitMQ publish and subscribe operations create linked spans across service boundaries
- [ ] `traceparent` header propagates trace context through RabbitMQ messages
- [ ] `total-orders` counter increments on each order creation, visible in Prometheus
- [ ] `products-per-order` histogram records distribution with configured buckets
- [ ] `/metrics` endpoint on each service returns Prometheus-scrapable data
- [ ] Jaeger and Prometheus run in Docker Compose

---

## Phase 11: Resilience — Retries & Backoff

**User stories**: 19, 20

### What to build

Add resilience to SQL connections via EF Core's `EnableRetryOnFailure(maxRetryCount: 5, maxRetryDelay: 40s)` with built-in exponential backoff. Add resilience to event publishing via a Polly `ResiliencePipeline` that handles `BrokerUnreachableException` and `SocketException` with configurable exponential backoff. Both resilience strategies are configured in the shared library's startup extensions so all microservices benefit.

### Acceptance criteria

- [ ] SQL connections retry up to 5 times with max 40-second delay on transient failures
- [ ] Event publishing retries with exponential backoff when RabbitMQ is temporarily unavailable
- [ ] Polly pipeline handles `BrokerUnreachableException` and `SocketException`
- [ ] Services recover gracefully when SQL Server or RabbitMQ come back online after a brief outage
- [ ] Retry behavior is configured in the shared library, not duplicated per microservice

---

## Phase 12: Auth Microservice & JWT Security

**User stories**: 11, 21

### What to build

An Auth microservice on port 8003 with a `User` model persisted in SQL Server via EF Core. A `POST /login` endpoint accepts credentials and returns a JWT token via `JwtTokenService` (HMAC-SHA256, 15-min expiry, `user_role` claim). The shared library provides `AddJwtAuthentication()` and `UseJwtAuthentication()` extension methods. Protected endpoints on Product, Order, and Basket microservices add `.RequireAuthorization()`. Role-based access restricts product creation/update to the `Administrator` role.

### Acceptance criteria

- [ ] `POST /login` with valid credentials returns `{ token, expiresIn }`
- [ ] `POST /login` with invalid credentials returns 401
- [ ] JWT token contains `user_role` claim
- [ ] Product `POST` and `PUT` endpoints require authentication and `Administrator` role
- [ ] Order endpoints require authentication (any role)
- [ ] Requests without valid JWT token receive 401
- [ ] Requests with valid token but insufficient role receive 403
- [ ] `AddJwtAuthentication()` shared extension configures consistent JWT validation across all services

---

## Phase 13: API Gateway with Ocelot

**User stories**: 12, 21, 27

### What to build

An API Gateway on port 8004 using Ocelot. All client requests route through the gateway to downstream microservices via `ocelot.json` configuration. Authentication is centralized at the gateway — the gateway validates JWT tokens and enforces role-based route claims before forwarding requests. Each microservice has its own solution and Dockerfile, enabling independent CI/CD. Downstream services remain in a private network accessible only through the gateway.

### Acceptance criteria

- [ ] `/login` routes to Auth service (unauthenticated)
- [ ] `/product/{id}` routes to Product service `GET` (unauthenticated)
- [ ] `/product/{everything}` routes to Product service `POST`/`PUT` (requires `Administrator` role)
- [ ] `/basket/{everything}` routes to Basket service (authenticated)
- [ ] `/order/{everything}` routes to Order service (authenticated)
- [ ] Gateway validates JWT and enforces `RouteClaimsRequirement` before forwarding
- [ ] All microservices are independently buildable and deployable (separate solutions + Dockerfiles)
- [ ] Gateway is the only publicly exposed service

---

## Phase 14: Kubernetes Deployment

**User stories**: 22, 23

### What to build

Docker Compose orchestrates the full system for local development (10 services). Kubernetes manifests deploy every service and infrastructure dependency to a cluster. Each microservice gets a Deployment + ClusterIP (internal) + LoadBalancer (external). Infrastructure services (SQL Server, RabbitMQ, Redis, Jaeger, Prometheus) get their own manifests with appropriate PersistentVolumeClaims. Environment variables reference Kubernetes service DNS names. The API Gateway's `ocelot.json` routes point to ClusterIP service names. Images are built and pushed to Docker Hub.

### Acceptance criteria

- [ ] `docker compose up` starts all 10 services and the system is functional end-to-end
- [ ] `kubectl apply -f` deploys all manifests successfully
- [ ] SQL Server data persists across pod restarts via PVC (200Mi at `/var/opt/mssql/data`)
- [ ] Microservices resolve infrastructure via Kubernetes DNS (e.g., `rabbitmq-clusterip-service`, `mssql-clusterip-service`)
- [ ] API Gateway routes to microservices via ClusterIP service names
- [ ] `kubectl get pods` shows all pods running
- [ ] `kubectl logs -f deployment/<name>` shows service logs
- [ ] End-to-end flow works through the gateway LoadBalancer: login → create product → add to basket → create order → verify basket cleared
