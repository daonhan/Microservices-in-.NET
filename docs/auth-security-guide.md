# .NET Microservice Auth Guide

JWT security guidance scoped to the `Auth.Service` in this repository and the
JWT validation shared by every consumer microservice. Read alongside the
reference page at [`wiki/Service-Auth.md`](wiki/Service-Auth.md), which
documents what the service does today; this document covers what is *unsafe*
about it and how to harden it.

Audience: maintainers of `auth-microservice/`, `shared-libs/ECommerce.Shared`,
and any service that calls `AddJwtAuthentication`.

---

## 1. Scope and Threat Model

The Auth service issues 15-minute HS256 access tokens to anyone who can
present valid credentials at `POST /login`. Every other microservice
(Payment, Product, Basket, Order, Inventory, Shipping, API Gateway) trusts
those tokens and authorizes requests using the `user_role` claim plus
in-handler ownership checks against `ClaimsPrincipal`.

The relevant attacker capabilities to design against:

1. **Network attacker on the public edge** — can call `/login` with stolen or
   guessed credentials, can replay captured tokens.
2. **Compromised consumer service** — can read `ECommerce.Shared` from disk
   and recover the symmetric signing key, then mint arbitrary tokens.
3. **Insider with database read** — can read the `Users` table and recover
   plaintext passwords directly.
4. **XSS on a frontend that consumes these tokens** — can exfiltrate any
   token reachable from JavaScript.
5. **Replay after revocation** — even if a user is removed, any unexpired
   token they hold remains valid.

The current implementation is vulnerable to (2), (3), (4), and (5) by
construction. Threat (1) is partially mitigated only by token expiry.

---

## 2. Current Implementation Summary

Mapping the codebase against the JWT security checklist:

| Concern | Current state | File |
|---|---|---|
| Signing algorithm | HS256 (symmetric) | `Services/JwtTokenService.cs:24` |
| Signing key | Hardcoded `const string SecurityKey` in shared package | `shared-libs/ECommerce.Shared/Authentication/AuthenticationExtensions.cs` |
| Algorithm whitelist on validation | Implicit via `JwtBearer` defaults — no explicit `ValidAlgorithms` | `AuthenticationExtensions.cs` |
| `iss` validation | Enabled, value from `AuthMicroserviceBaseAddress` | `AuthenticationExtensions.cs` |
| `aud` validation | **Disabled** (`ValidateAudience = false`) | `AuthenticationExtensions.cs` |
| `exp` validation | Enabled (default) | framework default |
| `nbf` validation | Not emitted, not enforced | — |
| `sub` claim | **Missing** — token only contains `name` and `user_role` | `JwtTokenService.cs` |
| `jti` claim | Missing — no unique token ID | `JwtTokenService.cs` |
| `iat` claim | Missing | `JwtTokenService.cs` |
| `kid` header | Missing — no key rotation support | `JwtTokenService.cs` |
| Password storage | **Plaintext** in `Users.Password` column | `Models/User.cs`, migration `20260414105802_InitialCreate.cs` |
| Login throttling | None | `Endpoints/AuthApiEndpoints.cs` |
| Account lockout after N failures | None | — |
| Refresh tokens | Not implemented | — |
| Revocation list | Not implemented | — |
| HTTPS enforcement on `/login` | Not in code (relies on gateway/ingress) | — |
| Constant-time credential comparison | No — `Username == username && Password == password` is a SQL `WHERE`, but hash comparison would also need `CryptographicOperations.FixedTimeEquals` | `Infrastructure/Data/EntityFramework/AuthContext.cs` |
| Token clock-skew tolerance | Default 5 minutes, not tuned | framework default |
| Token transmitted via | `Authorization: Bearer` (correct) | client-side convention |

Issues marked in **bold** are exploitable today, not theoretical. The rest
are gaps relative to defence-in-depth.

---

## 3. Prioritized Findings

### 3.1 (Critical) Plaintext password storage

`Users.Password` stores credentials as plaintext, including the seeded admin
in `UserConfiguration.cs`:

```csharp
builder.HasData(new User
{
    Id = new Guid("d854813c-4a72-4afd-b431-878cba3ecf2a"),
    Username = "microservices@daonhan.com",
    Password = "oKNrqkO7iC#G",
    Role = "Administrator"
});
```

A read of the `Users` table from any operator, replicated backup, or stolen
disk image hands over every credential in the system. Any salted, slow
password hash (Argon2id, bcrypt, PBKDF2) blocks this entirely.

**Fix.** Replace the column type and the verification path. Add a
`PasswordHash` column (drop `Password`) and use ASP.NET Core Identity's
`PasswordHasher<TUser>`:

```csharp
// Models/User.cs
public class User
{
    public Guid Id { get; set; }
    public required string Username { get; set; }
    public required string PasswordHash { get; set; }
    public required string Role { get; set; }
}

// Infrastructure/Data/EntityFramework/AuthContext.cs
private readonly PasswordHasher<User> _hasher = new();

public async Task<User?> VerifyUserLogin(string username, string password)
{
    var user = await Users.FirstOrDefaultAsync(u => u.Username == username);
    if (user is null)
    {
        // Run a dummy hash verification to keep timing roughly constant
        _hasher.VerifyHashedPassword(new User
        {
            Username = "",
            PasswordHash = "AQAAAAIAAYagAAAAEDummyHashToBurnCpuCyclesXXXX==",
            Role = ""
        }, "AQAAAAIAAYagAAAAEDummyHashToBurnCpuCyclesXXXX==", password);
        return null;
    }

    var result = _hasher.VerifyHashedPassword(user, user.PasswordHash, password);
    return result == PasswordVerificationResult.Success
        || result == PasswordVerificationResult.SuccessRehashNeeded
        ? user : null;
}
```

The dummy verification in the "user not found" branch matters: without it,
a username-enumeration attacker can distinguish "user does not exist" from
"user exists, wrong password" by measuring response time.

The seed migration must be replaced — generate the hash once with
`new PasswordHasher<User>().HashPassword(user, "...")` at design time and
paste the resulting string into `HasData`. Do **not** call the hasher
inside `OnModelCreating`, because EF needs the literal value to detect
migration drift.

### 3.2 (Critical) Symmetric signing key shipped in a shared library

> **Resolved:** This finding was resolved by migrating to RS256 with a JWKS endpoint, and dropping HS256 validation entirely in Phase 5.

`AuthenticationExtensions.SecurityKey` is a `public const string` compiled
into `ECommerce.Shared.dll`, which is published to a NuGet feed and copied
into every consumer container image. Any container compromise — including
read access to the local NuGet feed at `local-nuget-packages/` — yields
the key. With the key, an attacker can forge tokens with any `user_role`
including `Administrator` and any custom claim.

A symmetric scheme additionally makes consumers full forgery oracles: there
is no asymmetry between "trusted to issue" and "trusted to validate".

**Fix.** Switch to RS256 (or ES256 for smaller tokens). Place the private
key only in the Auth service. Distribute the public key to consumers via
a JWKS endpoint that the validation pipeline fetches on startup and caches.

Auth-side issuance:

```csharp
// Services/JwtTokenService.cs
private readonly RsaSecurityKey _signingKey;       // private key, Auth only
private readonly string _keyId;

public async Task<AuthToken?> GenerateAuthenticationToken(string username, string password)
{
    var user = await _authStore.VerifyUserLogin(username, password);
    if (user is null) return null;

    var creds = new SigningCredentials(_signingKey, SecurityAlgorithms.RsaSha256)
    {
        Key = { KeyId = _keyId }
    };

    var now = DateTime.UtcNow;
    var token = new JwtSecurityToken(
        issuer: _issuer,
        audience: "ecommerce-platform",
        claims: new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new Claim(JwtRegisteredClaimNames.Iat,
                new DateTimeOffset(now).ToUnixTimeSeconds().ToString(),
                ClaimValueTypes.Integer64),
            new Claim("user_role", user.Role),
            new Claim(JwtRegisteredClaimNames.PreferredUsername, user.Username)
        },
        notBefore: now,
        expires: now.AddMinutes(15),
        signingCredentials: creds);

    var jwt = new JwtSecurityTokenHandler().WriteToken(token);
    return new AuthToken(jwt, 900);
}
```

Auth-side JWKS endpoint:

```csharp
// Endpoints/JwksEndpoints.cs
routeBuilder.MapGet("/.well-known/jwks.json", (RsaSecurityKey key, string kid) =>
{
    var parameters = key.Rsa.ExportParameters(false);
    return Results.Ok(new
    {
        keys = new[]
        {
            new
            {
                kty = "RSA",
                use = "sig",
                alg = "RS256",
                kid,
                n = Base64UrlEncoder.Encode(parameters.Modulus),
                e = Base64UrlEncoder.Encode(parameters.Exponent)
            }
        }
    });
}).AllowAnonymous();
```

Consumer-side validation:

```csharp
// shared-libs/ECommerce.Shared/Authentication/AuthenticationExtensions.cs
.AddJwtBearer(options =>
{
    options.Authority = authOptions.AuthMicroserviceBaseAddress; // enables JWKS discovery
    options.RequireHttpsMetadata = !env.IsDevelopment();
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidIssuer = authOptions.AuthMicroserviceBaseAddress,
        ValidateAudience = true,
        ValidAudience = "ecommerce-platform",
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidAlgorithms = new[] { SecurityAlgorithms.RsaSha256 }, // pin algorithm
        ClockSkew = TimeSpan.FromSeconds(30),
        RequireSignedTokens = true,
        RequireExpirationTime = true
    };
});
```

`Authority` plus framework defaults will fetch and cache the JWKS, honour
`kid` from the token header, and silently rotate when the JWKS document
publishes a new key. Pin `ValidAlgorithms` explicitly — without it, an
attacker who steals the public key (which is published) can forge a token
with `alg: HS256` using the public key bytes as the HMAC secret. This is
the classic algorithm-confusion attack and the framework default does not
block it.

### 3.3 (High) Audience validation is disabled

`ValidateAudience = false` means a token minted for service A is also
accepted by service B. If a separate microservice is added later with a
different trust boundary (an admin-only management API, a third-party
integration), the existing tokens already authenticate against it.

**Fix.** Set a constant audience claim at issuance and validate it in every
consumer. The example above uses `"ecommerce-platform"`. If you later need
finer separation, mint multiple audiences:
`new[] { "ecommerce-platform", "admin-api" }` and let consumers each list
the audiences they accept.

### 3.4 (High) Missing `sub`, `jti`, `iat` claims

The current token uses `JwtRegisteredClaimNames.Name` (set to username) but
no subject claim. Authorization handlers in Payment and Shipping reach for
`customerId` claims that don't exist (see
`Endpoints/PaymentApiEndpoints.cs` and
`Endpoints/ShippingApiEndpoints.cs`), so non-admin paths cannot succeed
today.

`jti` enables targeted revocation (section 3.6). `iat` enables
"sessions-issued-before-X-are-revoked" semantics. `sub` is the standard
identifier downstream services should key off of, not the username, because
usernames change.

**Fix.** Emit all three at issuance — see the snippet in section 3.2.
Update consumer authorization to read `sub` instead of `name`, and add a
`customer_id` claim if the user is bound to a customer record.

### 3.5 (High) No login throttling, no lockout

`AuthApiEndpoints.Login` accepts unlimited attempts per IP, per user, per
second. A botnet can run online password guessing at full network speed.

**Fix.** Two layers. Per-IP rate limit at the gateway or in this service
using `AddRateLimiter`:

```csharp
// Program.cs
builder.Services.AddRateLimiter(options =>
{
    options.AddFixedWindowLimiter("login", o =>
    {
        o.Window = TimeSpan.FromMinutes(1);
        o.PermitLimit = 5;
        o.QueueLimit = 0;
    });
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
});

app.UseRateLimiter();

// Endpoints/AuthApiEndpoints.cs
routeBuilder.MapPost("/login", Login).RequireRateLimiting("login");
```

Per-account lockout — track consecutive failures on the user row
(`FailedAttempts`, `LockedUntil`) and reject when `LockedUntil > now`. A
fixed window of 5/min per IP plus 10 failures = 15-minute lockout per user
is a reasonable starting point; tune against your legitimate user behaviour.

### 3.6 (Medium) No revocation path

If an admin's credential leaks, the only mitigation today is "wait 15
minutes". With `jti` plus a Redis-backed revocation set, the system can
invalidate a specific token immediately:

```csharp
// shared-libs/ECommerce.Shared/Authentication/RevocationCheck.cs
public sealed class RevocationCheck
{
    private readonly IConnectionMultiplexer _redis;
    public RevocationCheck(IConnectionMultiplexer redis) => _redis = redis;

    public async Task<bool> IsRevokedAsync(string jti)
        => await _redis.GetDatabase().KeyExistsAsync($"revoked:{jti}");

    public Task RevokeAsync(string jti, TimeSpan ttl)
        => _redis.GetDatabase().StringSetAsync($"revoked:{jti}", "1", ttl);
}
```

Wire the check into `JwtBearerEvents.OnTokenValidated`:

```csharp
options.Events = new JwtBearerEvents
{
    OnTokenValidated = async ctx =>
    {
        var jti = ctx.Principal?.FindFirst(JwtRegisteredClaimNames.Jti)?.Value;
        if (jti is null) { ctx.Fail("missing jti"); return; }

        var revocation = ctx.HttpContext.RequestServices
            .GetRequiredService<RevocationCheck>();
        if (await revocation.IsRevokedAsync(jti))
            ctx.Fail("token revoked");
    }
};
```

Set the Redis TTL equal to the token's remaining lifetime so the revocation
set self-prunes.

### 3.7 (Medium) No refresh token, 15-minute access token

15 minutes is a reasonable access-token lifetime, but without a refresh
flow every user re-enters credentials four times an hour. The pragmatic
shape is short-lived access token plus longer-lived rotating refresh
token, where the refresh token is a separate JWT (or opaque database row)
with its own `jti` and is rotated on every use.

```
POST /login        → { access_token (15m), refresh_token (7d) }
POST /token/refresh → { access_token (15m), refresh_token (7d, new jti) }
                       (old refresh jti added to revocation list)
```

Refresh-token reuse — the same refresh token presented twice — must
revoke the entire chain, which is the standard defence against stolen
refresh tokens.

### 3.8 (Low) Migrations run at startup in Development

`Program.cs` calls `app.MigrateDatabase()` when `IsDevelopment()`. Two pods
booting in parallel both call `Database.Migrate()` and race. This is not a
production path today, but if `IsDevelopment()` ever evaluates true in a
shared environment, an interrupted migration corrupts the schema.

**Fix.** Run migrations as a separate init container or build step. The
runtime container should never own schema state.

### 3.9 (Low) Clock skew default of 5 minutes

`Microsoft.IdentityModel.Tokens` defaults to a 5-minute skew tolerance
during `exp` validation. A leaked, just-expired token therefore stays
valid for an extra five minutes. Tighten to 30 seconds (see snippet in
3.2). All hosts should already be NTP-synchronized in any environment that
runs SQL Server.

---

## 4. Hardening Roadmap

The order below is what I would ship in this repository, smallest blast
radius first:

1. **Switch password storage to `PasswordHasher<User>`**. New migration,
   new seed hash, replace the single SQL comparison. Self-contained to
   the auth service.
2. **Pin `ValidAlgorithms`** in `AuthenticationExtensions.cs`. One-line
   change that blocks algorithm-confusion forgery without changing keys.
3. **Add `sub`, `jti`, `iat`, `aud` to issued tokens; enable audience
   validation**. Update consumer authorization to read `sub`. This is
   the largest change because every consumer's claim-reading code is
   touched.
4. **Add per-IP rate limit and per-account lockout to `/login`**. Pure
   auth-service change, no consumer impact.
5. **Move signing key to RS256 with a JWKS endpoint**. Coordinate roll-out:
   ship consumers that accept both old HS256 and new RS256 first, switch
   issuance to RS256, then drop HS256 from consumers. The shared library
   can hold both `TokenValidationParameters` lists during the transition
   and select via `kid`.
6. **Add Redis-backed revocation list**. Requires the consumer
   `JwtBearerEvents` hook in step 5 to be in place.
7. **Add refresh-token endpoint with rotation and reuse detection**. New
   table `RefreshTokens(Id, UserId, JtiHash, ExpiresAt, RevokedAt,
   ReplacedBy)`. New endpoint `POST /token/refresh`.

Steps 1, 2, and 4 land independently. Steps 3, 5, 6, 7 are sequenced.

---

## 5. Operational Notes

**Token transport.** Clients in this repo today receive `AuthToken.Token`
in JSON and attach it as `Authorization: Bearer <token>`. That is correct.
Do not log full tokens — `Microsoft.AspNetCore.Authentication.JwtBearer`
defaults to logging the token on validation failure at `Information` level.
Override the log level to `Warning` and write a custom
`JwtBearerEvents.OnAuthenticationFailed` that logs only the failure reason
and the `kid` if present.

**Token storage on the client.** If a browser frontend is added, prefer
HttpOnly+Secure+SameSite=Strict cookies set by an edge service over
`localStorage`. The current API Gateway can perform the cookie-to-bearer
exchange, keeping the JWT out of JavaScript reach.

**Key rotation procedure.** With RS256+JWKS in place, rotation is:

1. Generate a new RSA keypair, assign a fresh `kid`.
2. Add it to the JWKS document alongside the old key.
3. Wait for consumer JWKS caches to expire (`MetadataAddress` cache, 24h
   default — tune `RefreshInterval` and `AutomaticRefreshInterval`).
4. Switch issuance to the new `kid`.
5. Wait one full token TTL (15 minutes) so all in-flight tokens expire.
6. Remove the old key from the JWKS document.

This procedure works without any consumer redeploy because the public key
list is fetched dynamically.

**Disaster scenarios.**

- *Signing key compromise.* Generate a new keypair, rotate immediately,
  drop the old key from JWKS the same minute. All outstanding tokens are
  invalidated. Force re-login.
- *Database compromise.* If passwords are hashed (section 3.1), rotation
  is recommended but not urgent. Force-rotate the passwords of any
  high-privilege account. If passwords are still plaintext at the time
  of the breach, every account is compromised — assume reuse on other
  systems.
- *Lost refresh token chain.* Increment a `tokens_invalid_before`
  timestamp on the user row and reject any token with `iat` earlier than
  that timestamp inside `OnTokenValidated`.

---

## 6. Testing the Hardened Service

The existing `Auth.Tests/AuthApiEndpointsTests.cs` mocks `ITokenService`
and asserts response shape. After hardening, add tests at three layers:

**Unit — token shape.** Decode the produced JWT in tests and assert
`alg == "RS256"`, `header.kid` is non-empty, payload contains
`sub`, `aud`, `jti`, `iat`, `exp`, `nbf`, and that `exp - iat == 900`.

**Integration — validation rejects malformed tokens.** Spin up
`WebApplicationFactory<Program>` for any consumer service and post:

- A token with `alg: "none"` → expect 401.
- A token signed with HS256 using the public key as secret → expect 401.
- A token with the correct signature but `iss` mismatch → expect 401.
- A token with `aud` mismatch → expect 401.
- A token with `exp` in the past → expect 401.
- A revoked `jti` → expect 401.

**End-to-end — login throttling.** Hit `/login` with bad credentials
six times within a minute and expect the sixth response to be 429.

These tests stay green across implementation changes as long as the
contract holds, which is the property you want for security tests.

---

## 7. Quick Reference

The minimum-viable hardened token, after sections 3.1–3.4 are applied:

```
Header:
{
  "alg": "RS256",
  "typ": "JWT",
  "kid": "auth-2026-04"
}

Payload:
{
  "iss": "https://auth.ecommerce.local",
  "aud": "ecommerce-platform",
  "sub": "d854813c-4a72-4afd-b431-878cba3ecf2a",
  "jti": "0fa8c6d0-...-b1e3",
  "iat": 1761552000,
  "nbf": 1761552000,
  "exp": 1761552900,
  "user_role": "Administrator",
  "preferred_username": "microservices@daonhan.com"
}

Signature: RSASSA-PKCS1-v1_5 with SHA-256 over base64url(header) + "." + base64url(payload)
```

Validation parameters every consumer must enforce:

```csharp
new TokenValidationParameters
{
    ValidateIssuer = true,
    ValidIssuer = "https://auth.ecommerce.local",
    ValidateAudience = true,
    ValidAudience = "ecommerce-platform",
    ValidateLifetime = true,
    ValidateIssuerSigningKey = true,
    RequireSignedTokens = true,
    RequireExpirationTime = true,
    ValidAlgorithms = new[] { "RS256" },
    ClockSkew = TimeSpan.FromSeconds(30)
}
```

Anything weaker than this on a consumer reopens one of the findings above.
