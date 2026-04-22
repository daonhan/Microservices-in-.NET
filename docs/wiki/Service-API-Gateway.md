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
