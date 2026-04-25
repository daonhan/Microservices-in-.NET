# API Gateway

Single public entry point. Validates JWTs, enforces role policies, and proxies to the downstream service. Ships with **two reverse-proxy implementations** compiled into the same binary: **YARP** (default) and **Ocelot** (runtime-switchable fallback).

| | |
|---|---|
| **Port** | 8004 |
| **Source** | [`api-gateway/ApiGateway/`](https://github.com/daonhan/Microservices-in-.NET/tree/main/api-gateway/ApiGateway) |
| **Tests** | [`api-gateway/ApiGateway.Tests/`](https://github.com/daonhan/Microservices-in-.NET/tree/main/api-gateway/ApiGateway.Tests) |

## Switching providers

Config key `Gateway:Provider` (env var `Gateway__Provider`) selects the active proxy. Defaults to `Yarp`; `Ocelot` is the only other valid value. Unknown values fail fast at startup. The active provider is logged:

```
ApiGateway starting with provider=Yarp
```

Rollback instructions and full context live in the repo [README § API Gateway Provider](https://github.com/daonhan/Microservices-in-.NET#api-gateway-provider-yarp--ocelot) and in [`docs/prd/PRD-ApiGateway-Yarp.md`](https://github.com/daonhan/Microservices-in-.NET/blob/main/docs/prd/PRD-ApiGateway-Yarp.md).

## Route map

| Upstream pattern | Target service | Auth |
|---|---|---|
| `POST /login` | [Auth](Service-Auth) | public |
| `GET /product/{id}` | [Product](Service-Product) | public |
| `POST/PUT /product/**` | Product | Bearer + `Administrator` |
| `** /basket/**` | [Basket](Service-Basket) | Bearer |
| `** /order/**` | [Order](Service-Order) | Bearer |
| `GET /inventory` | [Inventory](Service-Inventory) | Bearer + `Administrator` |
| `GET /inventory/**` | Inventory | Bearer |
| `POST /inventory/{productId}/backorder` | Inventory | Bearer |
| `POST/PUT /inventory/**` | Inventory | Bearer + `Administrator` |
| `GET /shipping/by-order/**` · `GET /shipping/{id}` | [Shipping](Service-Shipping) | Bearer |
| `GET /shipping` · `POST /shipping/{id}/**` · `GET /shipping/{id}/quotes` | Shipping | Bearer + `Administrator` |
| `POST /shipping/webhooks/carrier/{carrierKey}` | Shipping | public (shared secret) |

Authoritative route config:
- YARP: `api-gateway/ApiGateway/Gateway/Yarp/` configuration
- Ocelot: [`api-gateway/ApiGateway/ocelot.json`](https://github.com/daonhan/Microservices-in-.NET/blob/main/api-gateway/ApiGateway/ocelot.json)

## Cross-cutting behavior

Both providers share:

- JWT Bearer validation (via `AddJwtAuthentication()` — see [Shared-Library](Shared-Library))
- `user_role` claim check for `Administrator` on protected routes
- `/health/live` and `/health/ready` endpoints
- Prometheus metrics on `/metrics`
- OTLP traces through the shared observability pipeline

Clients and ops tooling are unaffected by the YARP↔Ocelot switch.

## Combined Swagger UI

In Development and Staging the gateway exposes a single Swagger UI that aggregates every service behind a dropdown:

| Path | Purpose |
|---|---|
| `GET /swagger/index.html` | Combined Swagger UI — pick a service from the dropdown |
| `GET /swagger/<service>/v1/swagger.json` | Per-service spec, transformed and proxied from the downstream service. Slugs: `auth`, `product`, `basket`, `order`, `inventory`, `shipping` |

The gateway runs each downstream spec through a Gateway Spec Transformer that:

- rewrites `servers` to `[ { url: "/" } ]` so Try-it-out targets the gateway origin
- rewrites operation paths from internal to gateway-facing (e.g. Product `/{id}` → `/product/{id}`)
- drops operations the gateway does not route to
- maps each operation's `security` from its YARP `AuthorizationPolicy`: `Anonymous` → none, `Default` → Bearer, `AdminOnly` → Bearer + admin-claim note
- tags every operation with the service name

Routes are derived from `IProxyConfigProvider` at request time, so the YARP `appsettings.json` stays the single source of truth.

The combined UI's Authorize dialog accepts a Bearer token returned by `POST /login` and attaches it to every Try-it-out call across services. Per-service Swagger UIs on the individual service ports have been removed in favor of this single entry point.

`ASPNETCORE_ENVIRONMENT=Production` causes every `/swagger*` URL on the gateway to return 404.

Source: `api-gateway/ApiGateway/Gateway/SwaggerAggregation/`. Tests: `api-gateway/ApiGateway.Tests/Gateway/SwaggerAggregation/`. Background: [`docs/prd/PRD-ApiGateway-OpenApi-Aggregation.md`](https://github.com/daonhan/Microservices-in-.NET/blob/main/docs/prd/PRD-ApiGateway-OpenApi-Aggregation.md), [`docs/plans/openapi-gateway-swagger-aggregation.md`](https://github.com/daonhan/Microservices-in-.NET/blob/main/docs/plans/openapi-gateway-swagger-aggregation.md).
