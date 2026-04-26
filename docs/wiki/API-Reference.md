# API Reference

> **Deprecated — prefer the combined Swagger UI.** The gateway now serves a live, code-generated Swagger UI at [`http://localhost:8004/swagger`](http://localhost:8004/swagger) (Development/Staging only) that aggregates every service behind a dropdown. Paths, schemas, and auth requirements there are derived from the running services and never drift. This page is kept as a quick at-a-glance reference but will be retired once adoption settles.

Consolidated listing of every public HTTP endpoint exposed through the Gateway. For individual service deep-dives see the per-service pages.

> **Maintainer note** — when you add or change an endpoint, update this page in the same PR. The endpoint source files are the authoritative schema.

## Auth — [Service-Auth](Service-Auth)

| Method | Gateway route | Downstream | Auth |
|---|---|---|---|
| `POST` | `/login` | Auth `/login` | public |

## Product — [Service-Product](Service-Product)

| Method | Gateway route | Downstream | Auth |
|---|---|---|---|
| `GET` | `/product/{id}` | Product `/{id}` | public |
| `POST` | `/product` | Product `/` | Bearer + `Administrator` |
| `PUT` | `/product/{id}` | Product `/{id}` | Bearer + `Administrator` |

## Basket — [Service-Basket](Service-Basket)

| Method | Gateway route | Downstream | Auth |
|---|---|---|---|
| `GET` | `/basket/{customerId}` | Basket `/{customerId}` | Bearer |
| `POST` | `/basket/{customerId}` | Basket `/{customerId}` | Bearer |
| `PUT` | `/basket/{customerId}` | Basket `/{customerId}` | Bearer |
| `DELETE` | `/basket/{customerId}/{productId}` | Basket `/{customerId}/{productId}` | Bearer |
| `DELETE` | `/basket/{customerId}` | Basket `/{customerId}` | Bearer |

## Order — [Service-Order](Service-Order)

| Method | Gateway route | Downstream | Auth |
|---|---|---|---|
| `POST` | `/order/{customerId}` | Order `/{customerId}` | Bearer |
| `GET` | `/order/{customerId}/{orderId}` | Order `/{customerId}/{orderId}` | Bearer |

## Inventory — [Service-Inventory](Service-Inventory)

| Method | Gateway route | Downstream | Auth |
|---|---|---|---|
| `GET` | `/inventory` | Inventory `/` | Bearer + `Administrator` |
| `GET` | `/inventory/{productId}` | Inventory `/{productId}` | public |
| `GET` | `/inventory/{productId}/movements` | Inventory `/{productId}/movements` | Bearer + `Administrator` |
| `POST` | `/inventory/{productId}/restock` | Inventory `/{productId}/restock` | Bearer + `Administrator` |
| `POST` | `/inventory/{productId}/backorder` | Inventory backorder handler | Bearer |

## Shipping — [Service-Shipping](Service-Shipping)

| Method | Gateway route | Downstream | Auth |
|---|---|---|---|
| `GET` | `/shipping/by-order/{orderId}` | Shipping `/by-order/{orderId}` | Bearer |
| `GET` | `/shipping/{shipmentId}` | Shipping `/{shipmentId}` | Bearer |
| `GET` | `/shipping` | Shipping `/` | Bearer + `Administrator` |
| `POST` | `/shipping/{shipmentId}/pick` | Shipping `/{shipmentId}/pick` | Bearer + `Administrator` |
| `POST` | `/shipping/{shipmentId}/pack` | Shipping `/{shipmentId}/pack` | Bearer + `Administrator` |
| `POST` | `/shipping/{shipmentId}/cancel` | Shipping `/{shipmentId}/cancel` | Bearer + `Administrator` |
| `POST` | `/shipping/{shipmentId}/deliver` | Shipping `/{shipmentId}/deliver` | Bearer + `Administrator` |
| `POST` | `/shipping/{shipmentId}/fail` | Shipping `/{shipmentId}/fail` | Bearer + `Administrator` |
| `POST` | `/shipping/{shipmentId}/return` | Shipping `/{shipmentId}/return` | Bearer + `Administrator` |
| `GET` | `/shipping/{shipmentId}/quotes` | Shipping `/{shipmentId}/quotes` | Bearer + `Administrator` |
| `POST` | `/shipping/{shipmentId}/dispatch` | Shipping `/{shipmentId}/dispatch` | Bearer + `Administrator` |
| `POST` | `/shipping/webhooks/carrier/{carrierKey}` | Shipping `/webhooks/carrier/{carrierKey}` | None (shared secret) |

## Payment — [Service-Payment](Service-Payment)

| Method | Gateway route | Downstream | Auth |
|---|---|---|---|
| `GET` | `/payment/by-order/{orderId}` | Payment `/by-order/{orderId}` | Bearer |
| `GET` | `/payment/{paymentId}` | Payment `/{paymentId}` | Bearer |
| `POST` | `/payment/{paymentId}/capture` | Payment `/{paymentId}/capture` | Bearer + `Administrator` |
| `POST` | `/payment/{paymentId}/refund` | Payment `/{paymentId}/refund` | Bearer + `Administrator` |

## Cross-cutting endpoints (every service)

| Route | Purpose |
|---|---|
| `/health/live` | Liveness — process is up |
| `/health/ready` | Readiness — dependencies reachable (SQL / Redis / RabbitMQ) |
| `/metrics` | Prometheus scrape |

These are wired via `MapPlatformHealthChecks()` and `AddPlatformObservability()` from [ECommerce.Shared](Shared-Library).
