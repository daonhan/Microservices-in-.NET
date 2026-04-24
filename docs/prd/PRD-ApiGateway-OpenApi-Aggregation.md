# PRD: OpenAPI Aggregation at the Gateway (Combined Swagger UI)

[#9](https://github.com/daonhan/Microservices-in-.NET/issues/9)

## Problem Statement

Developers working on this repo have no reliable way to see the system's public API surface. Today API discovery means opening a hand-maintained markdown table (`docs/wiki/API-Reference.md`) that silently drifts from code, grepping YARP routes out of `appsettings.json`, and reading individual service controllers to learn request/response shapes. Two of the six services (Basket and Order) ship their own Swagger UIs on separate dev ports at mismatched Swashbuckle versions, always-on regardless of environment, and they show **internal** per-service paths (`GET /{id}`) rather than the **gateway-facing** paths clients actually call (`GET /product/{id}`). They also bypass JWT and the gateway's authorization policies entirely, so "Try it out" in those service-local UIs does not exercise the real runtime. The remaining four services (Auth, Product, Inventory, Shipping) expose no OpenAPI document at all. Onboarding is slow, "does this endpoint exist?" is a scavenger hunt, and documentation rots on every controller change.

## Solution

Introduce a **combined Swagger UI hosted at the API Gateway** (`http://localhost:8004/swagger`, enabled in Development and Staging only) with a service-picker dropdown listing all six services. Each service produces its own OpenAPI document via a new shared-libs helper (`AddPlatformOpenApi`) that normalizes Swashbuckle version, JWT security scheme, document name, and XML-comment pickup. The gateway exposes one proxy route per service under `/swagger/<service>/v1/swagger.json` (Anonymous policy) that fetches the service's raw spec and pipes it through a **Gateway Spec Transformer** — a pure, unit-testable module that rewrites paths to the gateway-facing shape, filters out operations the gateway does not route to, annotates each operation with the gateway's authorization policy (`Anonymous` / `Default` / `AdminOnly`), and fixes the `servers` entry to the gateway origin so "Try it out" always goes through the real routing + auth path. The UI's Authorize button accepts a Bearer token obtained from `POST /login`. No document merging — each service stays its own spec — so there is no cross-service schema collision and per-service ownership is preserved.

## User Stories

### Developer

1. As a **new backend engineer**, I want to open one URL and see every endpoint the gateway exposes, so that I can learn the public API surface without reading YARP config.
2. As a **frontend engineer**, I want to try the live API from a browser with my JWT, so that I can validate request/response shapes before writing client code.
3. As a **backend engineer**, I want each service's spec generated from its own controllers, so that docs never drift from code.
4. As a **backend engineer on any service team**, I want to add OpenAPI to my service with a single `builder.AddPlatformOpenApi()` call, so that I don't have to re-solve JWT security scheme wiring every time.
5. As a **developer**, I want the combined UI to show paths as the gateway exposes them (e.g. `GET /product/{id}`), not internal service paths (`GET /{id}`), so that the docs match what I'll put in my client.
6. As a **developer**, I want endpoints that the gateway does NOT route to hidden from the combined UI, so that I don't try calls that 404 at the gateway.
7. As a **developer**, I want "Try it out" requests to go through `localhost:8004`, so that I exercise real routing and auth policies rather than bypassing them.
8. As a **developer**, I want to select a service from a dropdown inside the UI, so that I can flip between Auth, Product, Basket, Order, Inventory, and Shipping without opening multiple tabs.
9. As a **developer**, I want the gateway to discover specs from the same cluster addresses it already uses for routing, so that there is no second source of truth to keep in sync.
10. As a **developer writing controllers**, I want XML doc comments on my actions to flow into the Swagger UI, so that descriptions live next to the code.
11. As a **developer**, I want the Authorize button to accept a Bearer token and attach it to "Try it out" calls, so that I can exercise authenticated endpoints without a separate HTTP client.
12. As a **developer**, I want each operation tagged by service, so that tags act as an additional grouping inside a single service's spec.
13. As a **developer**, I want the gateway-rewritten spec to advertise the gateway as its only server, so that "Try it out" cannot accidentally hit a service's direct port.
14. As a **developer**, I want the combined UI available in Docker Compose and `dotnet run` modes identically, so that local workflows don't diverge.
15. As a **developer**, I want spec proxy routes to use the Anonymous authorization policy, so that the UI can load docs before I have a token.
16. As a **developer**, I want the combined UI URL and auth instructions documented in the repo README, so that I can find it without asking.

### Service owner

17. As an **Auth service owner**, I want `/login` documented with its request and response schema, so that consumers know how to obtain a JWT in the first place.
18. As an **Inventory service owner**, I want admin-only routes visibly marked in the combined UI, so that non-admin consumers don't waste time on them.
19. As a **service owner**, I want the aggregation layer to not require editing the gateway every time my team adds a new endpoint, so that we can ship independently.

### Operator / Platform

20. As an **Ops engineer**, I want the Swagger UI disabled in Production, so that we don't publish our full API surface to the internet by default.
21. As a **platform engineer**, I want one Swashbuckle version pinned across the repo, so that we don't debug JSON-schema differences between services.

### QA

22. As a **QA engineer**, I want the UI to reflect the gateway's auth policies (Anonymous, Default, AdminOnly), so that I understand which endpoints need a token and which need admin claims.

### Testing

23. As a **developer**, I want a regression test that fails if the combined UI or any service spec stops loading, so that a silent break surfaces in CI rather than when I open the browser.
24. As a **developer**, I want the spec transformer logic unit-tested in isolation, so that path-rewrite and filter edge cases (`{id}` vs `{**rest}` routes, method-specific policies) are provably correct.

## Implementation Decisions

**Aggregation style.** Multi-spec Swagger UI: one UI at the gateway with a service dropdown. Each service keeps its own OpenAPI document — no document merging — which avoids cross-service schema collision and preserves per-service ownership.

**Scope.** All six services (Auth, Basket, Inventory, Order, Product, Shipping) produce an OpenAPI document via the shared helper. Basket and Order's existing Swashbuckle setup is replaced by the helper to normalize version and configuration.

**Shared OpenAPI helper.** A new `ECommerce.Shared.OpenApi` module exposes `AddPlatformOpenApi` / `UsePlatformOpenApi` extension methods mirroring the existing `AddJwtAuthentication` / `AddPlatformObservability` / `AddPlatformHealthChecks` pattern. It registers Swashbuckle with a single pinned version, sets document name `v1`, declares the JWT Bearer security scheme, picks up XML doc comments if present, and applies a default service-name tag. Services consume it with one line in `Program.cs`.

**Per-service OpenAPI exposure.** Each service exposes `/swagger/v1/swagger.json` (Swashbuckle default). The individual service's Swagger **UI** is disabled — the UI is now gateway-only. Spec exposure is gated to non-Production environments.

**Gateway spec proxy.** YARP routes are added under `/swagger/<service>/v1/swagger.json`, each proxying to `http://<service-cluster>:8080/swagger/v1/swagger.json` with `AuthorizationPolicy: Anonymous` so the UI can load docs before login. A YARP response transform pipes each proxied response through the Gateway Spec Transformer before returning it to the browser.

**Gateway Spec Transformer (deep module).** A pure function: `(OpenApiDocument rawSpec, IReadOnlyList<GatewayRouteInfo> gatewayRoutes) → OpenApiDocument transformed`. Behavior:

- Only operations whose (HTTP method, internal path after the YARP `PathPattern` transform) map back to a gateway route are kept.
- Each kept operation's path is rewritten to the gateway-facing path (e.g. Product's `GET /{id}` becomes `GET /product/{id}`).
- Each operation's `security` requirement is set based on the gateway's `AuthorizationPolicy` for that route: `Anonymous` → no security; `Default` → Bearer; `AdminOnly` → Bearer with an admin-claim note in the operation description.
- `servers` is rewritten to `[ { url: "/" } ]` so "Try it out" always targets the gateway origin.
- Operations are tagged with the service name.

The module takes no dependency on YARP internals beyond a plain `GatewayRouteInfo` DTO derived from `IProxyConfigProvider`, which is the seam that keeps it unit-testable in isolation.

**Combined Swagger UI.** The gateway registers Swashbuckle's Swagger UI middleware at `/swagger` with one endpoint per service pointing at the proxied, transformed spec URL. JWT Bearer is wired as the security scheme; the Authorize button accepts a token obtained from `POST /login`.

**Environment gating.** The spec proxy routes and the combined UI are registered only when the host environment is Development or Staging. Production gateway binaries serve no content at `/swagger` (404).

**Route discovery.** The transformer's `GatewayRouteInfo` list is built from YARP's `IProxyConfigProvider` snapshot at request time, so the rewritten spec always reflects the live route table and does not drift from configuration.

**API contract for the gateway.** Two public URL shapes are introduced:

- `GET /swagger/index.html` — combined Swagger UI (dev + staging only).
- `GET /swagger/<service>/v1/swagger.json` — transformed OpenAPI document for a given service (dev + staging only, Anonymous policy).

**What this PRD does not change.** YARP route definitions in `appsettings.json`. JWT issuance or `/login` behavior. Per-service controller behavior (optional controller attributes / XML docs may be added for better docs, but no runtime changes).

## Testing Decisions

Good tests here assert **external behavior**: what the transformed spec contains given known inputs, and whether the UI and spec endpoints load. Avoid tests that pin Swashbuckle internals or verify middleware registration order — those break on minor package upgrades without catching real regressions. Prefer pure unit tests on the transformer over integration tests on Swashbuckle itself.

Modules under test:

1. **Gateway Spec Transformer** — pure unit tests. Cover path rewrite for literal routes (`/product/{id}`), catchall routes (`/basket/{**rest}`), method-specific filters (Inventory GET vs POST having different policies), operations not routed by the gateway being dropped, security applied per `AuthorizationPolicy`, `servers` rewrite, and tag application. Framework and style follow the existing xUnit conventions in the repo's test projects.

2. **Combined UI end-to-end smoke test** — integration test. Boots the gateway with an in-memory YARP configuration against stub downstream hosts that serve canned OpenAPI documents. Asserts `GET /swagger/index.html` returns 200 HTML, `GET /swagger/<service>/v1/swagger.json` returns 200 JSON for every configured service, and that an unrouted path in the canned downstream doc is absent from the transformed output. Prior art: the gateway already exposes `public partial class Program { }` as a `WebApplicationFactory` hook, matching the pattern used in existing gateway integration tests.

3. **Shared `AddPlatformOpenApi` helper** — integration test. Registers the helper in a minimal test host, fetches `/swagger/v1/swagger.json`, and asserts: document name is `v1`, JWT Bearer security scheme is present, XML comments are picked up when the XML file exists alongside the assembly.

## Out of Scope

- Merging all services into a single OpenAPI document (explicitly rejected in favor of the multi-spec dropdown).
- Alternative UIs (Scalar, Redoc) — Swagger UI only for this PRD.
- Automated OpenAPI spec diffing / breaking-change detection in CI (no `.github/` workflows exist in this repo today; worth a followup).
- Removing or deprecating the manually-maintained `docs/wiki/API-Reference.md` — documentation cleanup is a separate effort once the combined UI has been in use for a while.
- Production exposure of the combined UI. Current decision is dev + staging only; any later production exposure will need an auth-gating decision (AdminOnly policy or similar).
- OAuth2 / OIDC flow inside Swagger UI. Initial version uses a paste-the-Bearer-token Authorize dialog. A "login via /login" helper is a possible followup.
- Consumer-driven contract tests between services.
- Kubernetes Ingress changes for `/swagger`. The gateway's existing LoadBalancer already exposes it; no new ingress work.

## Further Notes

- Swashbuckle pinning: choose the latest stable at implementation time and set it centrally (e.g. `Directory.Packages.props`) if available; otherwise pin via the shared project's `.csproj`. Basket and Order's current 6.5.0 / 6.6.1 pins are replaced.
- The transformer's `GatewayRouteInfo` DTO is the seam that keeps the module pure. Shape it minimally: `RouteId`, `GatewayPathPattern`, `Methods`, `InternalPathPattern`, `AuthorizationPolicy`, `ClusterId`.
- The Auth service's OpenAPI must include `POST /login` request/response schema explicitly — it is the onboarding front door for the Authorize flow.
- Add a one-line note to the root `README.md` under "How to run locally" pointing at `http://localhost:8004/swagger` after `docker compose up --build`.
- Once this ships and teams adopt it, `docs/wiki/API-Reference.md` becomes redundant; coordinate the wiki deprecation with whoever owns it rather than deleting unilaterally.
