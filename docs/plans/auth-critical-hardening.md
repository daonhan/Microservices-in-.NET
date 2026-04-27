# Plan: Auth Service Critical Hardening

> Source PRD: [`docs/prd/PRD-Auth-Critical-Hardening.md`](../prd/PRD-Auth-Critical-Hardening.md) — PRD issue [#13](https://github.com/daonhan/Microservices-in-.NET/issues/13), plan issue [#14](https://github.com/daonhan/Microservices-in-.NET/issues/14)

## Architectural decisions

Durable decisions that apply across all phases. Locked before any phase begins.

- **Routes**:
  - `POST /login` — unchanged contract; request and response shapes preserved.
  - `GET /.well-known/jwks.json` — new, anonymous, served by Auth. Response conforms to RFC 7517 §5.
- **Schema**: one new EF migration in the Auth database. `Users.Password` (plaintext) is dropped; `Users.PasswordHash` (`nvarchar(max)`, required) replaces it. The seeded admin row is updated with a precomputed PBKDF2 hash. The migration's `Down` recreates the `Password` column with empty values — rollback to plaintext is intentionally lossy.
- **Key models**:
  - `User` aggregate: `Id`, `Username`, `PasswordHash`, `Role`. No transition column, no `IsHashed` flag, no fallback path.
  - `IRsaKeyProvider` abstraction in Auth: loads the active RSA private key plus the set of public keys to publish (active + previous, for rotation). Default impl reads PEM from disk; test impl injects in-memory keypairs.
- **Signing**: HS256 (symmetric, key in `ECommerce.Shared`) → RS256 (asymmetric, private key in Auth, public key served via JWKS). `kid` is required on every issued token. `ValidAlgorithms` is pinned to `["RS256"]` on the validation side to block algorithm confusion.
- **Hashing**: ASP.NET Core Identity `PasswordHasher<User>` at `IdentityV3` defaults (PBKDF2-HMAC-SHA512, 100k iterations, 256-bit salt). Registered as a singleton.
- **Trust model**: Auth is the only service holding the signing private key. Every consumer holds only public keys, fetched from `/.well-known/jwks.json` via `JwtBearerOptions.Authority`. The `public const string SecurityKey` in `ECommerce.Shared.Authentication.AuthenticationExtensions` is removed; reintroduction is blocked by a reflection-based contract test.
- **Versioning of `ECommerce.Shared`**: 1.18.0 (current) → **1.19.0** (dual-validator: accept HS256 or RS256) → **2.0.0** (RS256-only; major bump because public surface drops `SecurityKey`). Versions are published to the in-repo `local-nuget-packages/` feed.
- **Rollout**: dual-validator window. Ship `ECommerce.Shared` 1.19.0 to every consumer **before** flipping Auth issuance. After Auth flips and one full token TTL (15 min + safety margin) elapses, ship 2.0.0 dropping HS256 acceptance. Cutover gated on metric `jwt-validation-success` showing zero successful HS256 validations across all consumers in a 24-hour window.
- **Dev/CI keys**: deterministic RSA keypair committed to `auth-microservice/Auth.Service/dev-keys/`. `appsettings.Development.json` points to it. Production deployments override `Authentication:Signing:PrivateKeyPath` via environment variable. `dev-keys/` is excluded via `.dockerignore` so release images do not carry the dev key.
- **Token claim shape** (this PRD): `name`, `user_role`, `iss`, `exp` — preserved exactly as today. Adding `sub`, `jti`, `iat`, `nbf`, `aud` is **out of scope** and ships as the follow-up finding 3.4 PRD; the issuance code is restructured here so adding them later is a localized edit.

---

## Phase 1: Password hashing

**User stories**: 1, 5, 6, 10, 11

### What to build

Replace plaintext password storage and verification with `PasswordHasher<User>`. End-to-end slice in the Auth service: schema migration drops `Password` and adds `PasswordHash`, the seeded admin row is updated with a precomputed hash, `AuthContext.VerifyUserLogin` fetches by username and verifies against the hash, and the not-found branch runs a dummy hash verification to equalize timing. The `/login` endpoint contract is unchanged. After this phase, the seeded admin still logs in with the documented dev password (`oKNrqkO7iC#G`), but a database read no longer reveals usable credentials.

This phase is fully repo-local. No consumer changes. No `ECommerce.Shared` change.

### Acceptance criteria

- [ ] `Microsoft.AspNetCore.Identity` package referenced from `Auth.Service.csproj`. No `UserManager`, `IdentityDbContext`, or scaffolded UI is adopted.
- [ ] EF migration `DropPlaintextPasswordAddPasswordHash` exists. `Up` drops `Password`, adds `PasswordHash` (required), updates seeded admin row with precomputed hash. `Down` recreates `Password` with empty values.
- [ ] `User` model exposes `PasswordHash` (required). The `Password` property is gone.
- [ ] `AuthContext.VerifyUserLogin(username, password)` queries by username only, runs `PasswordHasher<User>.VerifyHashedPassword`, returns the user on `Success` or `SuccessRehashNeeded`, returns null otherwise.
- [ ] Unknown username path runs `VerifyHashedPassword` once against a constant dummy hash. Verified by a test double on `PasswordHasher<User>` asserting one invocation on the unknown-username path.
- [ ] `PasswordHasher<User>` registered as a singleton in `Program.cs`.
- [ ] Unit tests in `Auth.Tests` cover: correct credentials → user, wrong password → null, unknown username → null + dummy hash invoked.
- [ ] Existing `AuthApiEndpointsTests` continue to pass unchanged.
- [ ] `docker compose up --build auth` boots; `POST /login` with `microservices@daonhan.com / oKNrqkO7iC#G` returns 200 with a token; the same call with any other password returns 401.
- [ ] Reading the `Users` table directly (any SQL client) shows `PasswordHash` is a non-empty hash string, never plaintext.

---

## Phase 2: RSA key infrastructure and JWKS endpoint (Auth still issues HS256)

**User stories**: 3, 12, 17, 18

### What to build

Add the issuance plumbing for RS256 to the Auth service without changing what tokens it produces. New `Authentication:Signing` config section, new `IRsaKeyProvider` abstraction with a PEM-from-disk default implementation, dev keypair committed to `auth-microservice/Auth.Service/dev-keys/`, and a new `GET /.well-known/jwks.json` endpoint exposing the public key. `JwtTokenService` is restructured to take `IRsaKeyProvider` and a configured `KeyId` so the next phase can flip the signing call site in one place. Issuance still produces HS256 tokens — the existing `name` and `user_role` claims and 15-minute expiry are preserved exactly.

This phase is fully repo-local. No consumer changes yet. JWKS is served from day one of the rollout but consumers ignore it until Phase 3.

### Acceptance criteria

- [ ] `Authentication:Signing` config section with `PrivateKeyPath`, `KeyId`, optional `PreviousKeys` array of `{ KeyId, PublicKeyPath }`. Bound at startup.
- [ ] `IRsaKeyProvider` abstraction with default impl reading PEM from disk. Singleton lifetime.
- [ ] Dev RSA keypair (2048-bit minimum) committed under `auth-microservice/Auth.Service/dev-keys/`. `appsettings.Development.json` references the dev path. `.dockerignore` excludes `dev-keys/`.
- [ ] `GET /.well-known/jwks.json` returns the active key plus all `PreviousKeys`. Response shape conforms to RFC 7517 §5. Anonymous access. `Cache-Control: public, max-age=300`. A `jwks-served` counter is emitted.
- [ ] `JwtTokenService` constructor takes `IRsaKeyProvider`. Issuance call site is the single line that selects the algorithm — flippable in Phase 3 by changing `SecurityAlgorithms` constant only.
- [ ] Issuance still produces HS256 tokens; `name`, `user_role`, `iss`, `exp` (15 min) claims preserved exactly. No `sub`, `jti`, `iat`, `nbf`, `aud` yet.
- [ ] Endpoint test: `GET /.well-known/jwks.json` returns 200, body parses as JWKS, `keys[0].kid == configured KeyId`, `keys[0].n` and `keys[0].e` parse as a valid RSA public key, `Cache-Control` header present.
- [ ] No `ECommerce.Shared` version change. No consumer change.
- [ ] `docker compose up --build auth` boots; `GET http://localhost:8003/.well-known/jwks.json` returns the dev public key; `POST /login` continues to return HS256 tokens that consumers validate exactly as before.

---

## Phase 3: Dual-validator `ECommerce.Shared` 1.19.0 rolled to all consumers

**User stories**: 7, 8, 14, 15, 16

### What to build

Ship `ECommerce.Shared` 1.19.0, which accepts either the legacy HS256 key or RS256 via JWKS, and update every consumer service's `csproj` to reference it in the same slice. `AddJwtAuthentication` is extended to register a validator that accepts both algorithms and selects via `kid` (HS256 tokens have no `kid`; RS256 tokens always do). `JwtBearerOptions.Authority` is wired so the framework's metadata pipeline fetches and caches the JWKS from Auth. A `JwtBearerEvents.OnAuthenticationFailed` handler logs failure category + `kid` (never the full token), and a `jwt-validation-failure` counter with a category dimension is emitted. The `RequireHttpsMetadata` flag is `true` outside Development.

After this phase, every consumer can validate either flavor of token. Auth still issues HS256, so production traffic is unchanged. A demo can mint an RS256 token by hand against the dev key and confirm a consumer accepts it.

### Acceptance criteria

- [ ] `ECommerce.Shared` 1.19.0 published to `local-nuget-packages/`.
- [ ] `AddJwtAuthentication` accepts an `IHostEnvironment` parameter. `RequireHttpsMetadata` is `!env.IsDevelopment()`.
- [ ] `JwtBearerOptions.Authority` set to `AuthMicroserviceBaseAddress` so JWKS is fetched and cached automatically.
- [ ] `TokenValidationParameters` pinned: `ValidateIssuer = true`, `ValidateAudience = false`, `ValidateLifetime = true`, `ValidateIssuerSigningKey = true`, `RequireSignedTokens = true`, `RequireExpirationTime = true`, `ClockSkew = TimeSpan.FromSeconds(30)`. `ValidAlgorithms` includes both `HS256` and `RS256` for this version.
- [ ] `OnAuthenticationFailed` logs failure category (`expired`, `bad-signature`, `bad-issuer`, `algorithm-rejected`) and `kid` (when present). Full token never logged.
- [ ] `jwt-validation-failure` counter emitted with `category` dimension. `jwt-validation-success` counter emitted with `algorithm` dimension (`HS256` or `RS256`) — needed for the cutover gate in Phase 5.
- [ ] Every consumer service `csproj` (Payment, Product, Basket, Order, Inventory, Shipping, API Gateway) bumped to 1.19.0 and rebuilt.
- [ ] Integration test on a tiny test host: HS256 token signed with the legacy key → 200; manually-crafted RS256 token signed with the dev key → 200; token with `alg: "none"` → 401; token with bad signature → 401; token with future `nbf` (once issuance emits it) → 401.
- [ ] `docker compose up --build` brings up every consumer healthy; existing end-to-end smokes (checkout, payment, shipment) pass unchanged because Auth still issues HS256.

---

## Phase 4: Flip Auth issuance to RS256

**User stories**: 2, 4, 9, 12

### What to build

Change the single algorithm constant in `JwtTokenService` from `HmacSha256` to `RsaSha256` and ensure the `kid` is populated on every issued token. The `IRsaKeyProvider` plumbing from Phase 2 supplies the private key. The dual-validator from Phase 3 means consumers accept the new tokens immediately. Algorithm-confusion regression test is added in this phase because it first becomes meaningful here: a token signed with HS256 using the public key bytes as the secret must be rejected.

After this phase, all freshly minted tokens are RS256. In-flight HS256 tokens issued before the flip continue to validate (15-minute remaining lifetime).

### Acceptance criteria

- [ ] `JwtTokenService` issues RS256 tokens. `header.alg == "RS256"`, `header.kid == configured KeyId`. Signature verifies under the public key served at `/.well-known/jwks.json`.
- [ ] Claim list unchanged from Phase 2: `name`, `user_role`, `iss`, `exp`.
- [ ] Unit tests in `Auth.Tests` decode the produced JWT and assert `alg`, `kid`, signature verification, claims, and that `exp - now ≈ 900s`.
- [ ] Integration test on the consumer-side test host: a token signed with HS256 using the published public key bytes as the HMAC secret returns 401 (algorithm-confusion regression). Documented as "must remain green forever."
- [ ] Live smoke against running stack: `POST /login` returns a token; `jwt.io` (or equivalent) confirms `alg: RS256`, `kid` populated; consumer endpoints still authorize correctly.
- [ ] `jwt-validation-success` metric in Phase 3 reports `algorithm=RS256` for new tokens and `algorithm=HS256` only for in-flight tokens minted before the flip; the latter drains within 15 minutes plus safety margin.

---

## Phase 5: Drop HS256 — `ECommerce.Shared` 2.0.0

**User stories**: 2, 4, 13, 14

### What to build

Final cleanup. Ship `ECommerce.Shared` 2.0.0 with HS256 validation removed and `AuthenticationExtensions.SecurityKey` removed. `ValidAlgorithms` is pinned to `["RS256"]` only. Every consumer `csproj` bumps to 2.0.0 in the same slice. A reflection-based contract test in the consumer test project asserts `typeof(AuthenticationExtensions).GetField("SecurityKey", BindingFlags.Public | BindingFlags.Static) == null` so the constant cannot be reintroduced.

This phase is gated on telemetry: 24 consecutive hours with zero `jwt-validation-success{algorithm=HS256}` events across all consumers. If the gate is not met, the team investigates the lingering HS256 traffic before proceeding (likely a long-lived token from a stuck client).

### Acceptance criteria

- [ ] Cutover gate satisfied: 24h of zero HS256 successful validations across every consumer.
- [ ] `ECommerce.Shared` 2.0.0 published to `local-nuget-packages/`.
- [ ] `AuthenticationExtensions.SecurityKey` removed from the public surface. The compile breaks for any caller that still references it.
- [ ] `ValidAlgorithms = new[] { SecurityAlgorithms.RsaSha256 }`. HS256 is not accepted.
- [ ] Reflection-based contract test asserts `SecurityKey` field does not exist as a public static member of `AuthenticationExtensions`.
- [ ] Every consumer `csproj` bumped to 2.0.0 and rebuilt.
- [ ] End-to-end smoke: `POST /login` → token → consumer endpoint succeeds.
- [ ] Negative end-to-end smoke: a manually crafted HS256 token (using the old hardcoded key, recovered from git history) is rejected by every consumer.
- [ ] Documentation updated: `docs/auth-security-guide.md` section 3.2 is amended with a "Resolved" note linking to the merge commit.

---

## Phase ordering and rollback

Phases ship in order. Rollback rules per phase:

- **Phase 1 rollback**: revert the migration. `Down` recreates `Password` empty — every user must re-register. Acceptable only as emergency mitigation.
- **Phase 2 rollback**: trivially revert. JWKS endpoint stops being served; nothing depends on it yet.
- **Phase 3 rollback**: pin all consumer `csproj` files back to 1.18.0. Auth still issues HS256, so traffic is unaffected.
- **Phase 4 rollback**: change the algorithm constant back to `HmacSha256`. Consumers running 1.19.0 still accept both, so this is a one-line revert with no consumer rollback needed. **This is why Phase 4 is sequenced after Phase 3.**
- **Phase 5 rollback**: pin consumer `csproj` files back to 1.19.0. Auth keeps issuing RS256, consumers reaccept HS256 — no traffic impact, but the public surface still carries `SecurityKey` until the next 2.0.0 attempt.
