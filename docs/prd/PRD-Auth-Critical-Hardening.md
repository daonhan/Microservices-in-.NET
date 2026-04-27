# PRD — Auth Service Critical Hardening

> Tracking issue: [#13](https://github.com/daonhan/Microservices-in-.NET/issues/13)
> Implementation plan issue: [#14](https://github.com/daonhan/Microservices-in-.NET/issues/14)
> Source: [docs/auth-security-guide.md](../auth-security-guide.md), findings 3.1 and 3.2

## Context

The Auth service in `auth-microservice/Auth.Service/` issues 15-minute HS256 access tokens for the entire microservices platform. Two findings in [`docs/auth-security-guide.md`](../auth-security-guide.md) are flagged Critical and exploitable today:

- **3.1 Plaintext password storage.** `Users.Password` is a plain `nvarchar(max)` column. The seeded administrator (`microservices@daonhan.com / oKNrqkO7iC#G`) lives unhashed in `Migrations/20260414105802_InitialCreate.cs`. Any database read — operator query, replicated backup, stolen disk image — returns usable credentials.
- **3.2 Symmetric signing key shipped in `ECommerce.Shared`.** `AuthenticationExtensions.SecurityKey` is a `public const string` baked into the shared NuGet package. Every consumer container image carries it. Anyone who recovers the string from a single binary can forge `Administrator` tokens for every service.

These two together collapse the security model: a database leak hands over passwords, and a binary leak hands over forgery capability. Neither can be mitigated by token expiry, rate limiting, or any other secondary control. This PRD specifies the work to close both, sequenced so the high-impact, repo-local change (password hashing) ships first and the cross-service change (RS256 + JWKS) ships second behind a dual-validator transition.

## Problem Statement

As an operator of this microservices system, I cannot present this platform to a security review or to an external user because credentials are stored in plaintext and a single shared secret embedded in a downloadable NuGet package authorizes anyone to mint admin tokens for every service. Either compromise — database read or binary read — yields full system takeover with no way to detect or recover short of redeploying every service with a new key.

As a developer extending the platform, I cannot reason about which services are "trusted to issue" versus "trusted to validate" because every service holds the issuance secret. There is no asymmetry in the trust model.

As a future user, I expect that a breach of one component does not immediately compromise every other component. Today it does.

## Solution

Two coordinated changes against `auth-microservice/Auth.Service` and `shared-libs/ECommerce.Shared`:

1. **Replace plaintext passwords with `PasswordHasher<User>`.** Drop the `Password` column, add `PasswordHash`. `AuthContext.VerifyUserLogin` queries by username only and runs `PasswordHasher<User>.VerifyHashedPassword`. The seed migration stores a pre-computed hash of the existing dev password so docker-compose still boots with working credentials. Username-not-found path runs a dummy verification to keep response timing roughly constant.

2. **Migrate signing from HS256 to RS256 with a JWKS endpoint.** Auth holds an RSA private key loaded from configuration (file path or PEM env var). Auth exposes `GET /.well-known/jwks.json` with the public key. `ECommerce.Shared.AuthenticationExtensions` switches consumer validation to `JwtBearerOptions.Authority`, which fetches and caches the JWKS, honours `kid` from the token header, and pins `ValidAlgorithms = ["RS256"]` to block algorithm-confusion attacks. The `public const string SecurityKey` is removed.

   Rollout uses a dual-validator window: ship a release of `ECommerce.Shared` that accepts both the legacy HS256 key and the new RS256 JWKS, deploy that release to every consumer, then flip Auth to issue RS256, then ship a final `ECommerce.Shared` release that drops HS256 acceptance.

The `/login` endpoint contract does not change. `AuthToken { Token, ExpiresIn }` is unchanged. Consumer authorization code reads the same `user_role` claim. The token also gains `sub`, `jti`, `iat`, `nbf`, and `aud`, but reading them is out of scope here — that work belongs to the High-priority finding 3.4 PRD.

## User Stories

1. As an operator, I want stored passwords to be unrecoverable from a database dump, so that a backup leak does not equal a credential leak.
2. As an operator, I want the signing key for JWTs to live only in the Auth service, so that compromising any consumer service does not yield token-forgery capability.
3. As an operator, I want a documented, executable key-rotation procedure, so that I can rotate after a suspected compromise without redeploying every service.
4. As a security reviewer, I want algorithm confusion attacks to be blocked, so that publishing a public key cannot be used to forge HS256 tokens against it.
5. As a security reviewer, I want timing-side-channel username enumeration on `/login` to be infeasible, so that "user exists" cannot be inferred from response latency.
6. As a developer, I want the Auth service to compile and run locally with `docker-compose up` after the change, with the seeded admin still able to log in, so that my workflow does not break.
7. As a developer maintaining a consumer service, I want the `AddJwtAuthentication` call in my `Program.cs` to remain unchanged in shape, so that the shared-library upgrade is a NuGet bump rather than a rewrite.
8. As a developer, I want existing endpoint authorization (`.RequireAuthorization("Administrator")`, ownership checks against `ClaimsPrincipal`) to continue working without code changes, so that the change is contained to issuance and validation plumbing.
9. As a tester, I want an integration test that mints a token signed with HS256 using the published public key bytes as a secret and verifies it is rejected, so that the algorithm-confusion mitigation is regression-protected.
10. As a tester, I want an integration test that posts a valid plaintext password against the new endpoint and verifies it is rejected, so that the column drop is regression-protected (no fallback path).
11. As a tester, I want an integration test that asserts the seeded admin can still log in with the documented dev password, so that the migration's seed hash is correct.
12. As a tester, I want a unit test for `JwtTokenService` that decodes the produced JWT and asserts `header.alg == "RS256"`, `header.kid` is non-empty, and the signature verifies against the loaded public key.
13. As a tester, I want a contract test that fails if `ECommerce.Shared.AuthenticationExtensions.SecurityKey` reappears as a public symbol, so that the constant cannot accidentally be reintroduced.
14. As a release engineer, I want the cross-service change to ship in two NuGet versions of `ECommerce.Shared` (dual-validator, then RS256-only) so that I can deploy consumers and Auth independently without a coordinated cutover.
15. As an on-call engineer, I want JWKS fetch failures to be visible in logs and metrics, so that a misconfigured consumer surfaces immediately rather than silently rejecting all tokens.
16. As an on-call engineer, I want the existing `login-success` and `login-failure` counters to keep emitting unchanged values, so that my Grafana dashboards do not break.
17. As a CI maintainer, I want a deterministic dev RSA keypair checked into the repo for local and CI use, with production keys loaded from a separate path, so that integration tests run hermetically.
18. As a future maintainer of finding 3.4 (claim shape), I want the issuance code restructured so that adding `sub`, `jti`, `iat`, `aud` is a localized change to the claim list, so that 3.4 ships as a follow-up without rewriting issuance plumbing.

## Implementation Decisions

### Module 1: Password hashing in Auth service

- New dependency on `Microsoft.AspNetCore.Identity` for `PasswordHasher<TUser>`. No further Identity surface is adopted — no `UserManager`, no `IdentityDbContext`, no scaffolded UI. Rationale: the project already takes a hard dependency on EF Core 10 and ASP.NET Core 10; pulling `PasswordHasher<T>` is a single package and avoids hand-rolling PBKDF2/Argon2.
- `User.Password` is dropped; `User.PasswordHash` is added. There is no transition column, no `IsHashed` flag, no fallback path. The change is an EF migration that drops the old column, adds the new column, and re-seeds the admin user with the hashed value.
- `IAuthStore.VerifyUserLogin` keeps its current signature `Task<User?> VerifyUserLogin(string username, string password)`. The implementation moves from a single SQL `WHERE` to a SQL fetch by username plus an in-process hash verification. The not-found branch performs a dummy hash verification against a constant hash to equalize timing.
- Hash format: ASP.NET Core Identity `PasswordHasherCompatibilityMode.IdentityV3` (PBKDF2-HMAC-SHA512, 100k iterations, 256-bit salt). This is the framework default and is acceptable for the threat model; upgrading to Argon2id is out of scope.
- The seed hash is generated once at design time using a small one-off script invoked from the EF tools host, and the literal Base64 hash is pasted into `UserConfiguration.HasData`. Hashing inside `OnModelCreating` is rejected because it produces a different value on every migration scaffold and breaks EF's drift detection.
- `PasswordHasher<User>` is registered as a singleton. It is stateless and thread-safe.

### Module 2: RSA signing in Auth service

- New configuration section `Authentication:Signing` with three keys: `PrivateKeyPath` (file path to a PEM-encoded RSA private key), `KeyId` (a string, e.g. `auth-2026-04`), and an optional `PreviousKeys` array of `{ KeyId, PublicKeyPath }` for keys still served from JWKS during rotation.
- An `IRsaKeyProvider` abstraction loads the active private key plus the set of public keys to publish. The default implementation reads PEM from disk at startup; a test implementation lets integration tests inject in-memory keypairs. Singleton lifetime.
- `JwtTokenService` is changed to take `IRsaKeyProvider` and a configured `KeyId`. Token construction uses `SigningCredentials(rsaSecurityKey, SecurityAlgorithms.RsaSha256)` with `Key.KeyId` populated so the resulting JWT header contains `kid`. The current claim list (`name`, `user_role`) is preserved exactly; expiry remains 15 minutes. Adding `sub`, `jti`, `iat`, `aud` is deferred to the finding 3.4 PRD so this PRD lands without consumer changes.
- A new endpoint module `Endpoints/JwksEndpoints.cs` registers `GET /.well-known/jwks.json` returning the active public key plus any configured `PreviousKeys`. The endpoint is anonymous, cacheable for 5 minutes via `Cache-Control` header, and emits a `jwks-served` counter.
- The dev/CI keypair lives in `auth-microservice/Auth.Service/dev-keys/` and is committed to the repo. `appsettings.Development.json` points to it. Production deployments override `Authentication:Signing:PrivateKeyPath` via environment variable; the dev keys are never loaded in any non-Development environment.
- Auth service does not validate its own tokens, so no consumer-side `AuthenticationExtensions` change happens from the Auth side.

### Module 3: Consumer-side validation in `ECommerce.Shared`

- `AuthenticationExtensions.SecurityKey` is removed. `AddJwtAuthentication` no longer accepts a symmetric key.
- `AddJwtAuthentication` is rewritten to set `JwtBearerOptions.Authority = authOptions.AuthMicroserviceBaseAddress` so the framework's OpenID Connect metadata pipeline fetches `/.well-known/jwks.json` automatically, caches it (default 24h, configurable), and refreshes when a `kid` is unknown.
- `TokenValidationParameters` are pinned: `ValidateIssuer = true` (unchanged), `ValidateAudience = false` (unchanged in this PRD; tightened in 3.4), `ValidateLifetime = true`, `ValidateIssuerSigningKey = true`, `ValidAlgorithms = new[] { SecurityAlgorithms.RsaSha256 }`, `ClockSkew = TimeSpan.FromSeconds(30)`, `RequireSignedTokens = true`, `RequireExpirationTime = true`.
- `RequireHttpsMetadata` is `true` outside Development, `false` inside Development. The flag is wired through an `IHostEnvironment` parameter added to `AddJwtAuthentication`.
- A `JwtBearerEvents.OnAuthenticationFailed` handler logs the failure category (`expired`, `bad-signature`, `bad-issuer`, `algorithm-rejected`) and the `kid` from the failing token's header. The full token is not logged. A `jwt-validation-failure` counter is emitted with the failure category as a dimension.
- The version of `ECommerce.Shared` after this PRD is **2.0.0** (major bump because the public surface drops `SecurityKey`).

### Module 4: Dual-validator transition (intermediate `ECommerce.Shared` release)

- An intermediate `ECommerce.Shared` **1.19.0** release is published before Auth flips to RS256. `AddJwtAuthentication` in 1.19.0 registers two JWT bearer schemes (or a single scheme with multiple `IssuerSigningKeys`) that accept either the legacy HS256 key or RS256 via JWKS. Validation succeeds if any scheme accepts the token.
- 1.19.0 is deployed to every consumer service before Auth changes its issuance. This eliminates any cutover where consumers receive an RS256 token they cannot validate.
- After Auth ships RS256 issuance and all in-flight HS256 tokens have expired (one full token TTL = 15 minutes plus a safety margin), the **2.0.0** release of `ECommerce.Shared` is published, which removes the legacy HS256 acceptance.
- The transition is gated on metric `jwt-validation-success` segmented by algorithm; cutover proceeds to 2.0.0 only when no consumer reports a successful HS256 validation in a 24-hour window.

### Schema changes

- One new EF migration in Auth: `DropPlaintextPasswordAddPasswordHash`. Operations: drop `Users.Password`, add `Users.PasswordHash` (`nvarchar(max)`, required), update the seeded admin row with the precomputed hash. The migration's `Down` method recreates the `Password` column with empty values for every row — a true rollback to plaintext passwords is intentionally not supported.

### API contracts

- `POST /login` request and response shapes are unchanged.
- `GET /.well-known/jwks.json` is added. Response shape conforms to RFC 7517 §5.
- No consumer endpoint changes.

### Specific interactions

- During the dual-validator window, an Auth restart with a still-old key configuration continues to work because Auth's own issuance does not change until the explicit flip. JWKS is served by Auth from day one of the rollout (as soon as the RSA configuration is in place), but consumers ignore JWKS until they upgrade to 1.19.0.
- A consumer running 1.18.0 (legacy) against an Auth that has flipped to RS256 will hard-fail every request with 401. The rollout order — consumers to 1.19.0 first, then Auth flip — prevents this.

## Testing Decisions

A good test here verifies behaviour at the trust boundary. The rule is: assert what an attacker could do, not how the code is structured. Tests that assert `PasswordHasher<User>` is called are bad; tests that assert the response code on a known-bad input are good.

### Modules under test

- **`AuthContext.VerifyUserLogin`** — unit tests against an in-memory SQLite (matching prior art in other services) covering: correct credentials return the user, wrong password returns null, unknown username returns null and runs the dummy hash. The dummy-hash assertion is timing-insensitive — assert that `PasswordHasher.VerifyHashedPassword` is invoked exactly once on the unknown-username path via a test double on `PasswordHasher<User>`.
- **`JwtTokenService`** — unit tests that decode the produced JWT and assert: `header.alg == "RS256"`, `header.kid == configured KeyId`, signature verifies under the loaded public key, the `name` and `user_role` claims still appear, expiry is exactly 900 seconds from `iat`.
- **`AuthApiEndpoints.Login`** — existing tests pass unchanged. Add one new test: posting a valid `LoginRequest` returns a token whose `Token` is parseable as a JWT and whose header `alg == "RS256"`.
- **`/.well-known/jwks.json`** — endpoint test asserting: anonymous access succeeds, response shape matches RFC 7517, the active key's `kid` matches `Authentication:Signing:KeyId`, all listed keys parse as RSA public keys, response carries a `Cache-Control` header.
- **`ECommerce.Shared.AuthenticationExtensions`** — integration tests on a tiny test host that calls `AddJwtAuthentication` and asserts: a token with `alg: "none"` returns 401, a token signed with HS256 using the public key bytes as the secret returns 401 (algorithm-confusion regression), a token signed with the wrong RSA key returns 401, a token with a future `nbf` returns 401, a valid token returns 200.
- **Cross-package contract test** — a small reflection-based assertion in the test project: `typeof(AuthenticationExtensions).GetField("SecurityKey", BindingFlags.Public | BindingFlags.Static)` must return null. Prevents accidental reintroduction of the public constant.

### Prior art

- `Auth.Tests/AuthApiEndpointsTests.cs` — established pattern for endpoint-level testing without a host: invoke the static `Login` handler directly, mock `ITokenService` with NSubstitute, capture metrics with a `MeterListener`. The new `JwtTokenService` tests follow the same shape.
- `Shipping.Tests` and `Payment.Tests` — `IntegrationTestBase`, `WebApplicationFactory<Program>`, `TestAuthHandler` pattern for integration tests. Reuse for the JWKS endpoint test and the consumer-side validation tests; the consumer test host can be a one-off `WebApplicationFactory` over a minimal API that does nothing but call `.RequireAuthorization()`.
- The existing `Auth.Service.csproj` already exposes `internal` members to `Auth.Tests` via `InternalsVisibleTo`. Extend that to a new project `ECommerce.Shared.Tests` if it does not exist.

## Out of Scope

- Adding `sub`, `jti`, `iat`, `aud` claims to issued tokens. That is finding 3.4 (High) and ships separately because every consumer's claim-reading code is touched.
- Login throttling, per-IP rate limiting, account lockout. Finding 3.5 (High), separate PRD.
- Refresh tokens. Finding 3.7 (Medium), separate PRD.
- Redis-backed revocation list. Finding 3.6 (Medium), depends on `jti` from 3.4.
- Moving migrations out of startup. Finding 3.8 (Low), separate trivial change.
- Tightening clock skew below 30s. Finding 3.9 (Low), included implicitly in the Module 3 `TokenValidationParameters` because the dual-validator window already enforces 30s.
- Rotating the seeded admin password. The seed value is unchanged so existing local workflows keep working; rotation is an operator decision in any real deployment.
- Migrating to Argon2id or a non-Identity hasher. PBKDF2-SHA512 at framework defaults is acceptable for the current threat model.
- Adopting an external IdP (Azure AD, Auth0, Keycloak). The repo's purpose is to demonstrate self-hosted auth.

## Further Notes

The two findings are bundled because they share a deployment story (both touch Auth and `ECommerce.Shared`) and because shipping one without the other leaves a system that is still trivially compromised by the unfixed half. They could be split into two PRDs if the team prefers smaller units of review, with finding 3.1 shipping standalone first and finding 3.2 shipping after.

The `local-nuget-packages/` feed in the repo root is the publication target for both `ECommerce.Shared` 1.19.0 and 2.0.0 during local testing. Consumer microservices upgrade by bumping their `<PackageReference Include="ECommerce.Shared" Version="..." />` line and rebuilding their Docker image.

The dev RSA keypair committed to the repo is a deliberate convenience for local and CI runs. Production deployments must override the key path. The Dockerfile does not bake the dev key into release images — `dev-keys/` is excluded via `.dockerignore` to make the mistake harder.

After this PRD lands, finding 3.4 (claim shape) becomes the obvious next deliverable because the issuance code is already restructured around `JwtTokenService` taking `IRsaKeyProvider`, and adding claims to the existing claim list is a localized edit. Findings 3.6 (revocation) and 3.7 (refresh) depend on 3.4 (`jti`) and should be sequenced after it.
