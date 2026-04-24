# Plan: OpenAPI Aggregation at the Gateway (Combined Swagger UI)

> Source PRD: [docs/prd/PRD-ApiGateway-OpenApi-Aggregation.md](../docs/prd/PRD-ApiGateway-OpenApi-Aggregation.md) — tracked in [#9](https://github.com/daonhan/Microservices-in-.NET/issues/9)

## Architectural decisions

Durable decisions that apply across all phases:

- **Routes at the gateway**:
  - `GET /swagger/index.html` — combined Swagger UI, dev + staging only.
  - `GET /swagger/<service>/v1/swagger.json` — transformed OpenAPI document per service, dev + staging only, Anonymous policy.
  - Service slugs used in URLs: `auth`, `product`, `basket`, `order`, `inventory`, `shipping`.
- **Per-service spec endpoint**: `GET /swagger/v1/swagger.json` (Swashbuckle default), dev + staging only, UI disabled on the service itself.
- **Documentation generator**: Swashbuckle, single pinned version across the repo.
- **Security scheme**: JWT Bearer, declared identically by every service via the shared helper and re-advertised by the gateway's combined UI so a single Authorize entry covers every service.
- **Environment gate**: Registration of proxy routes, combined UI, and per-service spec is conditional on `IHostEnvironment.IsDevelopment() || IsStaging()`. Production returns 404 on every `/swagger*` URL.
- **Aggregation model**: Multi-spec dropdown. No cross-service document merging; each service remains its own document.
- **Spec transformer seam**: A pure function `(OpenApiDocument raw, IReadOnlyList<GatewayRouteInfo> routes) → OpenApiDocument transformed`, where `GatewayRouteInfo` is a minimal DTO with `RouteId`, `GatewayPathPattern`, `Methods`, `InternalPathPattern`, `AuthorizationPolicy`, `ClusterId`. No YARP types leak into this module.
- **Route discovery**: `GatewayRouteInfo` list is derived from `IProxyConfigProvider` at request time. YARP `appsettings.json` stays the single source of truth.
- **Transformer responsibilities** (established incrementally across phases):
  - Rewrite `servers` to `[ { url: "/" } ]`.
  - Rewrite operation paths from internal service path to gateway-facing path.
  - Drop operations the gateway does not route to.
  - Map each operation's `security` from the gateway's `AuthorizationPolicy`: `Anonymous` → none; `Default` → Bearer; `AdminOnly` → Bearer + admin-claim note in description.
  - Apply a service-name tag to each operation.
- **Shared helper**: `ECommerce.Shared.OpenApi.AddPlatformOpenApi` / `UsePlatformOpenApi`, mirroring `AddJwtAuthentication` / `AddPlatformObservability` / `AddPlatformHealthChecks`. Consumed by each service in one line.
- **Try-it-out target**: The gateway origin `/` only. Services do not expose Swagger UI on their own ports once onboarded.
- **Test framework**: xUnit, matching the repo's existing test projects. Gateway integration tests use `WebApplicationFactory<Program>` via the existing `public partial class Program { }` hook.
- **Out of scope for this plan**: Document merging; alternative UIs (Scalar, Redoc); OpenAPI spec diffing in CI; production exposure of the UI; OAuth2/OIDC flow inside Swagger UI; Kubernetes Ingress changes; deprecation of `docs/wiki/API-Reference.md`.

---

## Phase 1: Tracer bullet — Auth end-to-end

**User stories**: 1, 4, 8, 9, 14, 15, 17, 20

### What to build

Prove the full pipeline works end-to-end for a single service before scaling out. Introduce the shared `AddPlatformOpenApi` helper and consume it from the Auth service so `/login` is documented. Add one YARP proxy route at the gateway (`/swagger/auth/v1/swagger.json` → `auth-cluster`, Anonymous policy) that pipes the downstream spec through a minimal Gateway Spec Transformer. The transformer at this phase only rewrites `servers` to `/` and applies the service tag — no path rewriting or filtering yet, because `/login` is a literal passthrough with no prefix to strip. Register Swashbuckle's Swagger UI middleware at `/swagger` on the gateway with a single dropdown entry pointing at the proxied Auth spec. Gate registration (on the gateway and on Auth) to Development + Staging only.

End state: `docker compose up --build`, open `http://localhost:8004/swagger`, the dropdown shows "Auth", the Auth spec loads, "Try it out" on `POST /login` returns a valid JWT. `ASPNETCORE_ENVIRONMENT=Production` returns 404 at `/swagger`.

### Acceptance criteria

- [ ] `AddPlatformOpenApi` exists in shared-libs with JWT Bearer security scheme, document name `v1`, XML-comment pickup, and Swashbuckle pinned to a single version.
- [ ] Auth service calls `builder.AddPlatformOpenApi()` in `Program.cs` and exposes `GET /swagger/v1/swagger.json` (dev + staging only).
- [ ] Gateway exposes `GET /swagger/auth/v1/swagger.json` (Anonymous) that proxies and transforms the downstream spec: `servers` is `[ { url: "/" } ]` and every operation is tagged `auth`.
- [ ] Gateway exposes `GET /swagger/index.html` with Auth in the service dropdown.
- [ ] Combined UI's Authorize dialog accepts a Bearer token; `POST /login` via Try-it-out returns a token through the gateway.
- [ ] `/swagger*` paths on the gateway return 404 when `ASPNETCORE_ENVIRONMENT=Production`.

---

## Phase 2: Path rewriting, filtering, auth annotations (add Product)

**User stories**: 2, 5, 6, 7, 11, 13, 16, 22, 24

### What to build

Grow the transformer from a stub into the real deep module, using Product as the proving ground. Product is chosen because its gateway routes hit all three authorization policies simultaneously (`GET /product/{id}` anonymous, `POST/PUT /product/*` AdminOnly) and include a path rewrite (`/{id}` internal → `/product/{id}` gateway-facing). Extend the transformer to (a) rewrite each operation's path from the internal service path to the gateway-facing path by consulting the `GatewayRouteInfo` list, (b) drop operations the gateway does not route to, and (c) map each operation's `security` from its `AuthorizationPolicy`, including an admin-claim note in the description for `AdminOnly`. Onboard Product with the shared helper and add Product to the gateway's proxy + UI dropdown. Unit-test the transformer against a canned Product-shaped input covering all three behaviors. Ensure the Authorize button on the combined UI attaches the Bearer token to Try-it-out calls.

End state: the dropdown now lists Auth and Product. Product's anonymous read works without Authorize; admin-only writes require an admin token. Operations that exist on the Product service but are not routed through the gateway do not appear in the combined UI.

### Acceptance criteria

- [ ] Transformer rewrites internal paths to gateway-facing paths driven by `GatewayRouteInfo` (covers `/product/{id}`).
- [ ] Transformer drops operations whose (method, internal path) do not map back to a gateway route.
- [ ] Transformer sets operation `security`: `Anonymous` → none; `Default` → Bearer; `AdminOnly` → Bearer plus an admin-claim note in the description.
- [ ] Product service uses `AddPlatformOpenApi` and exposes `/swagger/v1/swagger.json` (dev + staging).
- [ ] Gateway exposes `/swagger/product/v1/swagger.json` and includes Product in the UI dropdown.
- [ ] Authorize in the combined UI attaches the Bearer token to Try-it-out requests; calling an AdminOnly endpoint with a non-admin token returns 403 through the gateway and with an admin token returns 200.
- [ ] xUnit tests on the transformer cover path rewrite, filter-out, and all three security mappings using canned OpenAPI inputs.

---

## Phase 3: Onboard remaining services, retire legacy Swashbuckle

**User stories**: 3, 10, 12, 18, 19, 21

### What to build

Fan out the established pattern to the remaining four services. Basket, Order, Inventory, and Shipping each consume `AddPlatformOpenApi` and gain a gateway proxy route + UI dropdown entry. As part of this phase, Basket's and Order's existing per-service Swagger setups (mismatched Swashbuckle 6.5.0 / 6.6.1 with always-on UI on ports 8000 / 8001) are removed in favor of the shared helper — the individual-service UIs go away because the UI is now gateway-only. This phase exercises transformer behavior not hit in Phase 2: catchall routes (`/basket/{**rest}`, `/order/{**rest}`, `/inventory/{**rest}`, `/shipping/{**rest}`) and method-specific policies on a single path (Inventory `GET` vs `POST`/`PUT`). Extend unit tests to cover these cases. Encourage, but do not require, XML doc comments on controllers at this stage — the shared helper picks them up automatically when present.

End state: all six services are listed in the dropdown. No service exposes its own Swagger UI anymore. Every gateway-exposed endpoint is visible in the combined UI with the correct auth annotation; service-internal operations not routed by the gateway are absent.

### Acceptance criteria

- [ ] Basket, Order, Inventory, Shipping each use `AddPlatformOpenApi`; their legacy Swashbuckle setup and per-service Swagger UI are removed.
- [ ] Gateway exposes `/swagger/basket/v1/swagger.json`, `/swagger/order/v1/swagger.json`, `/swagger/inventory/v1/swagger.json`, `/swagger/shipping/v1/swagger.json` and lists all six services in the UI dropdown.
- [ ] Catchall routes render correctly: e.g. Basket's internal `POST /{userId}/items` appears as `POST /basket/{userId}/items` in the combined UI.
- [ ] Inventory's method-specific policies render correctly: `GET /inventory/{id}` under `Default`, `POST/PUT /inventory/{id}` under `AdminOnly`, on the same gateway-facing path.
- [ ] Swashbuckle version is pinned to a single value across all services and the gateway.
- [ ] Transformer unit tests extended to cover catchall routes and method-specific policies on a shared path.

---

## Phase 4: End-to-end smoke test, README, production gate verification

**User stories**: 14, 16, 20, 23

### What to build

Lock in the behavior and make it discoverable. Add a gateway integration test (xUnit + `WebApplicationFactory<Program>`) that boots the gateway with an in-memory YARP configuration against stub downstream hosts serving canned OpenAPI documents, and asserts: `GET /swagger/index.html` returns 200 HTML; `GET /swagger/<service>/v1/swagger.json` returns 200 JSON for every configured service; an operation present in the canned downstream doc but not routed by the gateway is absent from the transformed output. Update the root `README.md` "How to run locally" section with the combined-UI URL and a one-paragraph Authorize walkthrough (hit `POST /login`, paste the returned token into Authorize). Add an integration test (or a run-book step) that verifies `ASPNETCORE_ENVIRONMENT=Production` causes `/swagger*` at the gateway to return 404. Add a short deprecation note near the top of `docs/wiki/API-Reference.md` pointing readers to the combined UI — do not delete the file in this phase.

End state: a CI-friendly test suite proves the combined UI and every per-service spec endpoint are reachable, production safety is verified, and any developer discovering the repo finds the combined UI URL in the README on first read.

### Acceptance criteria

- [ ] xUnit gateway integration test boots in-process against stub downstreams and asserts UI + per-service spec endpoints all 200, and that unrouted operations do not appear in transformed output.
- [ ] Root `README.md` "How to run locally" section links to `http://localhost:8004/swagger` with a short Authorize walkthrough.
- [ ] `ASPNETCORE_ENVIRONMENT=Production` is verified (test or documented run-book step) to return 404 on `/swagger/index.html` and every `/swagger/<service>/v1/swagger.json` at the gateway.
- [ ] `docs/wiki/API-Reference.md` carries a prominent deprecation note pointing to the combined UI; no deletion in this phase.
