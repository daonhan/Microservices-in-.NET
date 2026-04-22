# Plan: YARP-based API Gateway (Pluggable with Ocelot) [#4](https://github.com/daonhan/Microservices-in-.NET/issues/4)

> Source PRD: [docs/prd/PRD-ApiGateway-Yarp.md](../prd/PRD-ApiGateway-Yarp.md)  [#3](https://github.com/daonhan/Microservices-in-.NET/issues/3)

## Architectural decisions

Durable decisions that apply across all phases:

- **Upstream routes & port**: Gateway keeps listening on `8004`. Upstream paths stay identical to today's Ocelot config:
  - `POST /login`
  - `GET /product/{id}`, `POST|PUT /product/{**rest}`
  - `GET|POST|PUT|DELETE /basket/{**rest}`
  - `GET|POST /order/{**rest}`
  - `GET /inventory`, `POST /inventory/{productId}/backorder`, `GET /inventory/{**rest}`, `POST|PUT /inventory/{**rest}`
- **Downstream destinations**: Static Kubernetes ClusterIP DNS names on port `8080` (`auth-clusterip-service`, `product-clusterip-service`, `basket-clusterip-service`, `order-clusterip-service`, `inventory-clusterip-service`). HTTP only.
- **Path transform**: Strip the service prefix — `/<svc>/{**rest}` → `/{**rest}`. The `/inventory` listing route maps to downstream `/`.
- **Provider selection**: Single config key `Gateway:Provider` with values `Ocelot` | `Yarp`. Default is `Ocelot` until the rollout phase. Unknown values fail fast at startup.
- **Auth model**: Three authorization policies applied per route — `Anonymous`, `Default` (authenticated JWT), `AdminOnly` (JWT + role claim `user_role == "Administrator"`). JWT auth wired via existing `ECommerce.Shared.Authentication` before the gateway middleware.
- **YARP config shape**: Declarative in `appsettings.json` under the native `ReverseProxy` section (routes + clusters). Ocelot config in `ocelot.json` is left untouched.
- **Cross-cutting**: `ECommerce.Shared` observability, health checks, and Prometheus exporter are unchanged and shared by both providers.
- **Packaging**: Single project, both `Ocelot` and `Yarp.ReverseProxy` package references always compiled in. No new project, image, or port.

---

## Phase 1: Pluggable gateway seam (default = Ocelot, behavior unchanged)

**User stories**: 1, 5, 15, 16, 17, 18, 24

### What to build

Introduce the runtime-switchable seam that `Program.cs` will call instead of wiring Ocelot directly. Add the `Gateway:Provider` config key (default `Ocelot`) and a strongly-typed options type validated at startup. The seam has two branches: the Ocelot branch preserves today's exact behavior; the YARP branch throws `NotImplementedException`. No YARP config yet. Startup logs the active provider.

This is pure refactor + plumbing: the running gateway behaves identically to today when `Provider=Ocelot`, and fails fast with a clear error when `Provider=Yarp`. Unit tests cover the provider-selection logic (Ocelot branch wires Ocelot, YARP branch fails, unknown value fails validation).

### Acceptance criteria

- [ ] `appsettings.json` contains `"Gateway": { "Provider": "Ocelot" }`.
- [ ] `Program.cs` no longer references Ocelot directly; it calls the new seam for both service registration and middleware.
- [ ] Starting the gateway with `Provider=Ocelot` serves all existing routes exactly as before (manual smoke or existing route exercised end-to-end).
- [ ] Starting with `Provider=Yarp` fails fast at startup with a clear message.
- [ ] Starting with an unknown `Provider` value fails at startup via options validation.
- [ ] Startup log line includes the active provider name.
- [ ] Unit tests verify: Ocelot branch registers Ocelot-specific services; YARP branch does not register Ocelot services; invalid provider throws during validation.

---

## Phase 2: YARP tracer bullet — one anonymous route (`/login` → auth)

**User stories**: 2, 3, 14, 18, 21, 23

### What to build

Implement the YARP branch of the seam end-to-end for a single upstream route: `POST /login` → auth service. Add the `Yarp.ReverseProxy` package, a `ReverseProxy` section in `appsettings.json` with one route and one cluster, and the path-prefix transform. Register `MapReverseProxy` from inside the seam's YARP branch. Authorization policy for this route is `Anonymous`.

Stand up the provider-parametric integration test harness now, since it will be reused by every later phase: boot the gateway in-process with an in-memory downstream stub HTTP server, parameterize the test over `Provider=Ocelot` and `Provider=Yarp`, assert the stub received the expected method + downstream path.

### Acceptance criteria

- [ ] `Yarp.ReverseProxy` package reference added; project still builds with both Ocelot and YARP compiled in.
- [ ] `appsettings.json` contains a `ReverseProxy` section with one route (`login`) and one cluster (`auth-cluster`).
- [ ] With `Provider=Yarp`, `POST /login` is proxied to the auth cluster with path `/login`.
- [ ] With `Provider=Yarp`, no other routes are served (404), proving the single-route slice is isolated.
- [ ] Integration test harness boots the gateway in-process against an in-memory downstream stub.
- [ ] A parametric test asserts `POST /login` succeeds under both `Ocelot` and `Yarp`, and the stub records method `POST` and path `/login`.
- [ ] With `Provider=Ocelot`, all existing routes still work (regression guard).

---

## Phase 3: Authenticated routes under YARP

**User stories**: 10, 12, 13, 19, 20, 22

### What to build

Extend the YARP config to cover all routes that require an authenticated user (and the remaining anonymous route, `GET /product/{id}`):

- `GET /product/{id}` — Anonymous
- `GET|POST|PUT|DELETE /basket/{**rest}` — Default (authenticated)
- `GET|POST /order/{**rest}` — Default
- `POST /inventory/{productId}/backorder` — Default
- `GET /inventory/{**rest}` — Default

Register the `Default` authorization policy (authenticated user). Hook JWT authentication into the YARP pipeline so `AuthorizationPolicy` on routes is enforced. Path-prefix transform applies uniformly.

Extend the parametric test matrix to cover these routes, asserting the 200 / 401 matrix (no token → 401 on authenticated routes; valid token → 200; anonymous routes → 200 without a token), plus downstream path correctness for each.

### Acceptance criteria

- [ ] `appsettings.json` defines routes and clusters for product (read), basket, order, inventory read, and inventory backorder.
- [ ] `Default` authorization policy is registered and applied to the authenticated routes above.
- [ ] JWT middleware runs before YARP so anonymous requests to protected routes receive `401` under `Provider=Yarp`.
- [ ] Parametric tests assert each authenticated route: no token → `401`; valid user token → `200`; and the downstream stub received the expected transformed path.
- [ ] Parametric tests assert `GET /product/{id}` works anonymously under both providers.
- [ ] All Phase 2 tests still pass.

---

## Phase 4: Admin-only routes under YARP

**User stories**: 11, 20, 22

### What to build

Add the `AdminOnly` authorization policy (authenticated JWT + role claim `user_role == "Administrator"`) and apply it to the admin routes in the YARP config:

- `POST|PUT /product/{**rest}`
- `GET /inventory` (listing)
- `POST|PUT /inventory/{**rest}`

Ensure route ordering / method matching in the YARP config disambiguates admin-only variants from the authenticated-user variants on overlapping paths (notably `/inventory/{**rest}`, where GET is user-level and POST/PUT are admin-only; and the `/inventory/{productId}/backorder` POST which must remain user-level and not be shadowed by the admin `POST /inventory/{**rest}`).

Extend the parametric test matrix to cover the 200 / 401 / 403 behavior for admin routes under both providers.

### Acceptance criteria

- [ ] `AdminOnly` authorization policy is registered and applied to all admin routes per the routing parity matrix.
- [ ] YARP route match order / method filters correctly distinguish `POST /inventory/{productId}/backorder` (user) from `POST /inventory/{**rest}` (admin).
- [ ] Parametric tests assert for each admin route: no token → `401`; non-admin token → `403`; admin token → `200`; downstream stub receives the correct transformed path.
- [ ] Full routing matrix tests pass under both `Ocelot` and `Yarp`.

---

## Phase 5: Ops parity — health, metrics, observability, startup log

**User stories**: 5, 6, 7, 8

### What to build

Verify and, where needed, fix operational cross-cutting concerns so the YARP provider is indistinguishable from Ocelot to ops tooling:

- `/health` and `/health/ready` respond identically under both providers.
- Prometheus `/metrics` endpoint exposes metrics under both providers.
- OpenTelemetry traces emitted by the gateway include the downstream cluster and route so a request's full path is visible in Jaeger.
- Startup log line clearly identifies the active provider (already added in Phase 1; confirm wording and placement).

Add smoke-level parametric tests for `/health` and `/metrics`. Tracing is verified manually against the local OTel/Jaeger stack described in the repo's observability setup.

### Acceptance criteria

- [x] Parametric smoke tests assert `/health` returns `200` under both providers.
- [x] Parametric smoke tests assert `/metrics` returns `200` with Prometheus-format output under both providers.
- [x] A request through the YARP gateway appears in Jaeger with a span that identifies the downstream cluster/route.
- [x] Startup log line format reviewed; provider name is present and unambiguous.
- [x] Kubernetes liveness/readiness probes and Prometheus scrape config require no changes.

---

## Phase 6: Rollout — flip default to YARP + docs

**User stories**: 1, 4, 9

### What to build

Switch the default `Gateway:Provider` in `appsettings.json` from `Ocelot` to `Yarp`. Document the rollback procedure (flip the env var `Gateway__Provider=Ocelot` and restart) in the gateway README or top-level README. No code changes beyond the default value and docs.

### Acceptance criteria

- [ ] `appsettings.json` default `Gateway:Provider` is `Yarp`.
- [ ] Docs describe how to switch providers via env var / config, including the exact env var name (`Gateway__Provider`).
- [ ] Docs describe the rollback procedure to `Ocelot`.
- [ ] Full test suite (unit + parametric integration matrix) passes.
- [ ] Kubernetes and Docker Compose manifests require no edits to run with the new default.
