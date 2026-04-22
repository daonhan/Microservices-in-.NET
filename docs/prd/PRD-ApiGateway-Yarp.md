# PRD: YARP-based API Gateway (Pluggable with Ocelot)

[#3](https://github.com/daonhan/Microservices-in-.NET/issues/3)

## Problem Statement

The platform currently exposes all microservices (Auth, Product, Basket, Order, Inventory) through a single API Gateway built on **Ocelot 24.x** (`api-gateway/ApiGateway`). Ocelot works, but:

- It is effectively in maintenance mode and its release cadence lags behind ASP.NET Core.
- Its JSON-based routing config (`ocelot.json`) duplicates concepts that ASP.NET Core / `Microsoft.Extensions.Http` already model natively (endpoints, policies, named HTTP clients).
- Advanced scenarios (per-route transforms, load balancing across replicas, HTTP/2+gRPC passthrough, direct integration with `Microsoft.AspNetCore.Authorization` policies) are awkward.
- There is no clean way to A/B test a different gateway, or to fall back to Ocelot if YARP introduces a regression.

The operator wants to adopt **YARP** (Microsoft's reverse proxy library) as the primary gateway, **without deleting Ocelot**, so they can switch implementations via configuration and compare behavior in dev/staging before committing.

## Solution

Introduce a second gateway implementation backed by **YARP** inside the existing `api-gateway` project (or a sibling project in the same solution), and make the gateway runtime choose between `Ocelot` and `Yarp` based on a single configuration value (e.g. `Gateway:Provider`). Both implementations:

- Expose the **same upstream routes** (`/login`, `/product/...`, `/basket/...`, `/order/...`, `/inventory/...`) on the same host/port (`8004`).
- Reuse the existing shared platform cross-cutting concerns from `ECommerce.Shared` (JWT auth, OpenTelemetry, health checks, Prometheus exporter) unchanged.
- Proxy to the same Kubernetes ClusterIP service names (`auth-clusterip-service`, `product-clusterip-service`, etc.) on port `8080`.
- Enforce identical authentication and role-based authorization rules (e.g. `Administrator` for product writes and inventory listing/writes).

The YARP configuration lives in `appsettings.json` under a `ReverseProxy` section (YARP's native schema), so it is diff-able against `ocelot.json`. Switching providers is a single env var / config flip and a container restart — no code change, no new image.

## User Stories

### Operator / Platform

1. As a platform operator, I want to choose between Ocelot and YARP via a single config value, so that I can switch gateways without code changes.
2. As a platform operator, I want both gateways to expose identical upstream routes, so that clients and downstream services are unaffected by the switch.
3. As a platform operator, I want the YARP gateway to listen on the same port (`8004`) as Ocelot, so that Kubernetes manifests and Docker Compose do not need dual configurations.
4. As a platform operator, I want a documented rollback procedure to Ocelot, so that I can revert within minutes if YARP misbehaves in production.
5. As a platform operator, I want the gateway provider to be logged at startup, so that I can confirm which implementation is running.
6. As a platform operator, I want health checks (`/health`, `/health/ready`) to work identically under both gateways, so that liveness/readiness probes don't need per-provider tuning.
7. As a platform operator, I want Prometheus metrics exposed from the gateway under both providers, so that dashboards keep working.
8. As a platform operator, I want OpenTelemetry traces from the gateway to include the downstream service name and route, so that Jaeger shows a complete request path.

### Developer

9. As a developer, I want YARP routes and clusters defined declaratively in `appsettings.json`, so that I can review gateway changes in a pull request diff.
10. As a developer, I want YARP route authorization to be expressed with ASP.NET Core authorization policies, so that auth rules live next to the rest of the ASP.NET Core code.
11. As a developer, I want a single authorization policy for "Administrator role", so that admin-only routes (`POST/PUT /product/*`, `GET /inventory`, `POST/PUT /inventory/*`) declare it by name.
12. As a developer, I want a YARP path transform that strips the service prefix (e.g. `/product/{**catch-all}` → `/{**catch-all}`), so that downstream services keep their current path schemes.
13. As a developer, I want the `GET /product/{id}` route to remain anonymous, so that catalog browsing does not require a token.
14. As a developer, I want `POST /login` to be proxied anonymously to the Auth service, so that sign-in works for unauthenticated clients.
15. As a developer, I want to run the gateway locally in either mode with `dotnet run`, so that I can debug both implementations.
16. As a developer, I want a Directory.Build.props / csproj-level conditional package reference OR a single project that references both Ocelot and YARP, so that I don't fork the project.
17. As a developer, I want the `Gateway:Provider` value validated at startup (fail fast on unknown values), so that typos don't silently fall back to a default.

### Customer (unchanged behavior)

18. As a customer, I want all API calls to keep working at `http://<gateway>:8004`, so that my client does not break during the YARP migration.
19. As a customer, I want JWT tokens issued by the Auth service to be accepted by the gateway regardless of provider, so that my session survives a provider switch.
20. As a customer, I want 401/403 responses to remain consistent between providers, so that my client's error handling does not need to change.

### Testing

21. As a developer, I want an integration test suite that boots the gateway in-process and asserts upstream routing behavior, so that I can run it against either provider by flipping config.
22. As a developer, I want tests that assert anonymous vs. authorized vs. admin-only routes return `200` / `401` / `403` as expected, so that security regressions are caught.
23. As a developer, I want tests that assert the downstream URL transform (prefix stripping) is correct, so that downstream services receive the path they expect.
24. As a developer, I want the provider-selection logic unit-tested in isolation, so that I'm confident both branches wire up without booting the full app.

## Implementation Decisions

### Modules

- **`GatewayProviderOptions`** — Strongly typed options bound to the `Gateway` config section. Fields: `Provider` (enum: `Ocelot`, `Yarp`). Validated on startup (`ValidateOnStart`).
- **`GatewayProviderExtensions`** (deep module, testable) — A single pair of extension methods on `WebApplicationBuilder` / `WebApplication`:
  - `builder.AddConfiguredGateway()` — reads `Gateway:Provider`, calls either `AddOcelotGateway()` or `AddYarpGateway()`.
  - `app.UseConfiguredGateway()` — mirrors the above for the middleware pipeline.
  - This is the "pluggable" seam. All callers in `Program.cs` go through it; switching providers never touches `Program.cs`.
- **`OcelotGatewayModule`** — Encapsulates existing Ocelot wiring (`AddOcelot`, `UseOcelot`, `ocelot.json` load). No behavior change vs. today.
- **`YarpGatewayModule`** — Encapsulates YARP wiring: `AddReverseProxy().LoadFromConfig(...)`, `MapReverseProxy()`, transform registration, and authorization policy registration.
- **`GatewayAuthorizationPolicies`** — Central place defining policy names (`"AdminOnly"`) and their requirements (role claim `user_role == "Administrator"`). Reused by the YARP config via `AuthorizationPolicy` on each route.

### Configuration

- Add `Gateway:Provider` to `appsettings.json` with a default of `Ocelot` (preserves current behavior on upgrade).
- Add a `ReverseProxy` section (YARP native schema) to `appsettings.json` defining **routes** and **clusters** that mirror `ocelot.json` 1-for-1:
  - Routes: `login`, `product-read`, `product-write`, `basket`, `order`, `inventory-list`, `inventory-backorder`, `inventory-read`, `inventory-write`.
  - Clusters: `auth-cluster`, `product-cluster`, `basket-cluster`, `order-cluster`, `inventory-cluster`, each with a single destination `http://<svc>-clusterip-service:8080`.
- Route transforms use YARP's `PathPattern` / `PathRemovePrefix` to map `/product/{**catch-all}` → `/{**catch-all}`, matching Ocelot's current behavior.
- Route `AuthorizationPolicy` is set to `"AdminOnly"` for admin routes, `"Default"` (authenticated) for user routes, and `"Anonymous"` for `/login` and `GET /product/{id}`.

### Routing Parity Matrix

| Upstream | Methods | Auth | Downstream service | Downstream path |
|---|---|---|---|---|
| `/login` | POST | Anonymous | auth | `/login` |
| `/product/{id}` | GET | Anonymous | product | `/{id}` |
| `/product/{**rest}` | POST, PUT | AdminOnly | product | `/{**rest}` |
| `/basket/{**rest}` | GET, POST, PUT, DELETE | Authenticated | basket | `/{**rest}` |
| `/order/{**rest}` | GET, POST | Authenticated | order | `/{**rest}` |
| `/inventory` | GET | AdminOnly | inventory | `/` |
| `/inventory/{productId}/backorder` | POST | Authenticated | inventory | `/{productId}/backorder` |
| `/inventory/{**rest}` | GET | Authenticated | inventory | `/{**rest}` |
| `/inventory/{**rest}` | POST, PUT | AdminOnly | inventory | `/{**rest}` |

### Cross-Cutting (unchanged)

- JWT authentication via `ECommerce.Shared.Authentication.AddJwtAuthentication` — runs **before** the gateway middleware under both providers.
- Observability via `ECommerce.Shared.Observability.AddPlatformObservability("ApiGateway")`.
- Health checks via `ECommerce.Shared.HealthChecks.AddPlatformHealthChecks` / `MapPlatformHealthChecks`.
- Prometheus exporter stays mapped at the same path.

### Packaging

- Add `Yarp.ReverseProxy` package reference alongside the existing `Ocelot` reference. Both are always compiled in; selection is runtime.
- No new microservice, no new container image, no new port.
- Kubernetes `api-gateway.yaml` and Docker Compose entry remain unchanged; the provider is chosen via an env var (`Gateway__Provider`).

### Decisions deliberately made

- **Single project, runtime switch** (not two separate gateway projects). Rationale: keeps ops surface area small; the switch is meant to be temporary / comparative, not permanent.
- **Config-driven YARP** (not code-driven route registration). Rationale: parity of editing experience with `ocelot.json`; easier review.
- **`ocelot.json` stays as-is.** No attempt to auto-generate YARP config from Ocelot config.
- **Default provider = `Ocelot`** on first merge to avoid surprise behavior change; flipped to `Yarp` once the matrix tests pass in staging.

## Testing Decisions

### What makes a good test here

- Tests assert **external gateway behavior** (status codes, downstream URL received, auth outcome) — never YARP or Ocelot internals.
- Tests are **provider-parametric**: the same test body runs twice, once with `Gateway:Provider=Ocelot` and once with `Gateway:Provider=Yarp`, proving parity.
- Downstream services are replaced by an **in-memory stub HTTP server** that records the method and path it received, so assertions can check transform correctness without booting real microservices.

### Modules to test

- **`GatewayProviderExtensions`** — unit tests that verify:
  - `Provider=Ocelot` registers Ocelot services and middleware, not YARP's.
  - `Provider=Yarp` registers YARP services and middleware, not Ocelot's.
  - An unknown/missing provider value throws on startup (`ValidateOnStart`).
- **Routing parity (integration)** — one test per row in the routing matrix, asserting:
  - Correct downstream cluster is hit.
  - Path transform produces the expected downstream path.
  - Auth outcome: anonymous routes return `200` without a token; authenticated routes return `401` without a token; admin routes return `403` for a non-admin token and `200` for an admin token.
- **Health & metrics (smoke)** — `/health` returns `200` and `/metrics` exposes Prometheus output under both providers.

### Prior art

- `auth-microservice/Auth.Tests/AuthApiEndpointsTests.cs` already uses `WebApplicationFactory`-style endpoint testing against a minimal-API host — the same pattern applies here for the gateway.
- `basket-microservice/Basket.Tests/Endpoints/` demonstrates integration tests with stubbed collaborators; the in-memory downstream stub in this PRD follows the same philosophy.

### Modules the user wants tests for

- `GatewayProviderExtensions` (unit).
- Routing parity matrix (integration, parametric over provider).
- Auth enforcement per route (integration).

## Out of Scope

- Removing Ocelot. The Ocelot implementation stays until the operator explicitly decides to delete it in a follow-up PRD.
- Rewriting downstream microservices or changing their paths.
- Adding new gateway features that don't exist today (rate limiting, request aggregation, response caching, WebSockets, gRPC passthrough). These are tracked separately.
- Changing the JWT issuance flow or `ECommerce.Shared.Authentication`.
- Service discovery (Consul, Kubernetes EndpointSlices). Destinations remain static ClusterIP DNS names.
- Canary routing / traffic splitting between Ocelot and YARP on the same port. Switching is wholesale, not per-request.
- TLS termination changes. HTTP-only inside the cluster, as today.

## Further Notes

- The `Gateway:Provider` config key should also be surfaced in the startup log line (`ApiGateway starting with provider=Yarp`) to make rollbacks auditable.
- When the team is confident YARP is stable, a follow-up PRD will: (a) flip the default to `Yarp`, (b) remove Ocelot and `ocelot.json`, (c) simplify `GatewayProviderExtensions` back down to a single call.
- YARP supports `PassHttpContext`-style transforms; any claim-forwarding or header-stripping decisions should be revisited when we remove Ocelot, since YARP makes them trivial.
- The shared `ECommerce.Shared` package is already referenced and does not need changes for this PRD.
