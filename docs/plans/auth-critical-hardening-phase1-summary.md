# Phase 1 Summary — Password hashing

> Source plan: [`auth-critical-hardening.md`](./auth-critical-hardening.md) §Phase 1.
> Source PRD: [`docs/prd/PRD-Auth-Critical-Hardening.md`](../prd/PRD-Auth-Critical-Hardening.md) — PRD issue [#13](https://github.com/daonhan/Microservices-in-.NET/issues/13), plan issue [#14](https://github.com/daonhan/Microservices-in-.NET/issues/14).

## Goal

Replace plaintext password storage and comparison in the Auth service with `PasswordHasher<User>` (ASP.NET Core Identity `IdentityV3` defaults: PBKDF2-HMAC-SHA512, 100k iterations, 256-bit salt). End-to-end vertical slice: schema → seed → store → tests → docker smoke. `/login` request/response shape unchanged. Token issuance still HS256 — RS256 work belongs to Phase 2+.

User stories covered: 1, 5, 6, 10, 11.

## Scope boundary

Fully repo-local. **No** changes to:
- `ECommerce.Shared` (still 1.18.0).
- Any consumer service `csproj`.
- `JwtTokenService`, `Authentication:Signing` config, JWKS, key infra.
- `User` aggregate beyond swapping `Password` → `PasswordHash`. No `IsHashed` flag, no transition column, no fallback verification path.

## Current state (pre-Phase 1)

- `auth-microservice/Auth.Service/Models/User.cs:7` — `public required string Password { get; set; }` (plaintext).
- `auth-microservice/Auth.Service/Infrastructure/Data/EntityFramework/AuthContext.cs:21-23` — `VerifyUserLogin` does `Username == username && u.Password == password` (string equality, leaks timing + plaintext-equal lookup).
- `auth-microservice/Auth.Service/Infrastructure/Data/EntityFramework/Configurations/UserConfiguration.cs:22-29` — seeded admin row holds `Password = "oKNrqkO7iC#G"` plaintext.
- `auth-microservice/Auth.Service/Migrations/20260414105802_InitialCreate.cs` — `Users.Password nvarchar(max) NOT NULL`; seed inserts plaintext.
- `auth-microservice/Auth.Service/Auth.Service.csproj` — no `Microsoft.AspNetCore.Identity` reference yet.
- `auth-microservice/Auth.Tests/AuthApiEndpointsTests.cs` — endpoint-level tests using a substituted `ITokenService`. No `AuthContext.VerifyUserLogin` tests today.

## Deltas to land

### 1. Package reference

`auth-microservice/Auth.Service/Auth.Service.csproj` — add:

```xml
<PackageReference Include="Microsoft.AspNetCore.Identity" Version="..." />
```

Pull `PasswordHasher<TUser>` only. **Not** adopting `UserManager`, `IdentityDbContext`, scaffolded UI, or `IdentityUser`.

### 2. `User` aggregate

`auth-microservice/Auth.Service/Models/User.cs` — replace `Password` with `PasswordHash`:

```csharp
public class User
{
    public Guid Id { get; set; }
    public required string Username { get; set; }
    public required string PasswordHash { get; set; }
    public required string Role { get; set; }
}
```

### 3. EF mapping

`auth-microservice/Auth.Service/Infrastructure/Data/EntityFramework/Configurations/UserConfiguration.cs`:

- Drop the `Password` `IsRequired()` line, add `PasswordHash` `IsRequired()`.
- Update `HasData` seed row: replace `Password = "oKNrqkO7iC#G"` with `PasswordHash = "<precomputed PBKDF2 hash of 'oKNrqkO7iC#G' under IdentityV3>"`.

The precomputed hash is generated **once** by running `PasswordHasher<User>.HashPassword(adminUser, "oKNrqkO7iC#G")` against a deterministic test seed (see Open question 1 below) and pasted as a literal string. Must not be regenerated per build — EF will detect drift and produce a noisy migration.

### 4. Migration `DropPlaintextPasswordAddPasswordHash`

`auth-microservice/Auth.Service/Migrations/<timestamp>_DropPlaintextPasswordAddPasswordHash.cs`. Generated via `dotnet ef migrations add DropPlaintextPasswordAddPasswordHash` after the model + seed change.

`Up`:
- `DropColumn` `Password`.
- `AddColumn` `PasswordHash` `nvarchar(max) NOT NULL`.
- `UpdateData` row `d854813c-4a72-4afd-b431-878cba3ecf2a` setting `PasswordHash` to the precomputed literal.

`Down` (intentionally lossy, per plan §Architectural decisions):
- `DropColumn` `PasswordHash`.
- `AddColumn` `Password` `nvarchar(max) NOT NULL` with empty default — every user must re-register on rollback. Acceptable only as emergency mitigation.

### 5. `AuthContext.VerifyUserLogin`

`auth-microservice/Auth.Service/Infrastructure/Data/EntityFramework/AuthContext.cs:21-24` — fetch by username only, then verify hash:

```csharp
private static readonly string DummyHash = /* precomputed fixed hash, e.g. for "" under IdentityV3 */;

private readonly IPasswordHasher<User> _hasher;

public AuthContext(DbContextOptions<AuthContext> options, IPasswordHasher<User> hasher)
    : base(options)
{
    _hasher = hasher;
}

public async Task<User?> VerifyUserLogin(string username, string password)
{
    var user = await Users.FirstOrDefaultAsync(u => u.Username == username);

    if (user is null)
    {
        // Equalize timing — always run one hash verification on the unknown-username path.
        _hasher.VerifyHashedPassword(new User { Username = "", PasswordHash = DummyHash, Role = "" }, DummyHash, password);
        return null;
    }

    var result = _hasher.VerifyHashedPassword(user, user.PasswordHash, password);

    return result is PasswordVerificationResult.Success
                  or PasswordVerificationResult.SuccessRehashNeeded
        ? user
        : null;
}
```

Notes:
- `SuccessRehashNeeded` returns the user (login proceeds). Rehashing-on-login is **out of scope** for this PRD; seed already uses current defaults so the path is dormant.
- Constant `DummyHash` is a precomputed literal, not regenerated per call — generation cost would defeat the timing equalization.

### 6. DI

`auth-microservice/Auth.Service/Program.cs` — register `PasswordHasher<User>` as a singleton before the SQL Server datastore registration (so `AuthContext` constructor can resolve it):

```csharp
builder.Services.AddSingleton<IPasswordHasher<User>, PasswordHasher<User>>();
builder.Services.AddSqlServerDatastore(builder.Configuration);
```

`PasswordHasher<TUser>` defaults to `IdentityV3` and is thread-safe — singleton is correct.

### 7. Tests

`auth-microservice/Auth.Tests/AuthContextVerifyUserLoginTests.cs` (new). EF in-memory or SQLite-in-memory provider seeded with one user holding a known hash. `IPasswordHasher<User>` substituted via NSubstitute so the dummy-hash invocation can be asserted.

Cases:
- Correct credentials → returns the user; hasher invoked once with the seeded `PasswordHash`.
- Wrong password → returns null; hasher invoked once and returned `Failed`.
- Unknown username → returns null; hasher invoked **exactly once** against the constant dummy hash (timing-equalization assertion). This is the load-bearing test for user story 11.
- `SuccessRehashNeeded` → returns the user (regression guard for the `or` branch).

Existing `AuthApiEndpointsTests` continues to pass unchanged — it stubs `ITokenService`, doesn't touch `AuthContext`.

### 8. Docker smoke

Per plan acceptance criteria:
- `docker compose up --build auth` boots clean.
- `POST /login` `microservices@daonhan.com / oKNrqkO7iC#G` → 200 + token.
- Same with any other password → 401.
- Direct SQL `SELECT PasswordHash FROM Users` shows non-empty PBKDF2 hash, never plaintext, never `oKNrqkO7iC#G`.

## Critical files

**Modify**
- `auth-microservice/Auth.Service/Auth.Service.csproj`
- `auth-microservice/Auth.Service/Models/User.cs`
- `auth-microservice/Auth.Service/Infrastructure/Data/EntityFramework/Configurations/UserConfiguration.cs`
- `auth-microservice/Auth.Service/Infrastructure/Data/EntityFramework/AuthContext.cs`
- `auth-microservice/Auth.Service/Program.cs`

**Create**
- `auth-microservice/Auth.Service/Migrations/<timestamp>_DropPlaintextPasswordAddPasswordHash.cs` (+ `.Designer.cs`)
- Updated `auth-microservice/Auth.Service/Migrations/AuthContextModelSnapshot.cs` (regenerated by `dotnet ef`)
- `auth-microservice/Auth.Tests/AuthContextVerifyUserLoginTests.cs`

**Untouched**
- `JwtTokenService`, `TokenStartupExtensions`, all endpoints, all consumer services, `ECommerce.Shared`.

## Open questions

1. **Precomputed hash determinism.** `PasswordHasher<User>.HashPassword` includes a random salt — every invocation produces a different hash for the same password. The seeded literal is generated **once** and pasted; subsequent `dotnet ef` model checks must not regenerate it. Plan: write a tiny one-shot dev tool / xUnit `[Fact(Skip="manual")]` that prints a hash for the dev password, paste output into `UserConfiguration` + the migration. Alternative: derive the hash inside the migration `Up` via raw SQL + a deterministic salt — rejected, defeats `IdentityV3`'s salt design.

2. **`DummyHash` source.** Two options:
   - (a) Precompute one fixed PBKDF2 hash for empty string under IdentityV3 and inline it as a constant. **Recommended** — deterministic, matches user-found path's cost.
   - (b) Compute at startup from a fixed salt via reflection. Adds startup complexity, no real benefit.

3. **`AuthContext` constructor signature change.** Adding `IPasswordHasher<User>` to the constructor breaks any test that constructs `AuthContext` directly with `new AuthContext(options)`. Grep `auth-microservice/Auth.Tests` before merge — current snapshot shows none, but confirm. If consumers exist outside `Auth.Tests` (they shouldn't — `InternalsVisibleTo` is limited), provide a parameterless overload pulling the hasher from a service locator. **Not recommended** — prefer fixing call sites.

4. **EF in-memory vs SQLite-in-memory for the new test.** Repo precedent across other services should dictate. SQLite enforces NOT NULL and is closer to SQL Server semantics; EF in-memory is faster but skips constraints. Pick whichever the existing `*.Tests` projects already use to keep dev-loop friction low.

## Verification

- `dotnet build auth-microservice/Auth.Service` green.
- `dotnet test auth-microservice/Auth.Tests` green incl. `AuthContextVerifyUserLoginTests` and unchanged `AuthApiEndpointsTests`.
- `dotnet ef migrations script` from `InitialCreate` → `DropPlaintextPasswordAddPasswordHash` reviewed: `DROP COLUMN Password`, `ADD PasswordHash NOT NULL`, `UPDATE Users SET PasswordHash = '<literal>' WHERE Id = 'd854813c-...'`.
- Solution-wide `dotnet build` green (no consumer should be affected — sanity check).
- Manual smoke (per plan §Phase 1):
  - `docker compose up --build auth`.
  - `POST /login` known-good credentials → 200 + token.
  - `POST /login` known-bad password → 401.
  - SQL client: `SELECT PasswordHash FROM Users` → non-empty hash; no row contains `oKNrqkO7iC#G`.

## Rollback

Per plan §Phase ordering and rollback: revert the migration. `Down` recreates `Password` empty — every user must re-register. Acceptable only as emergency mitigation. No consumer or `ECommerce.Shared` rollback needed because Phase 1 doesn't touch them.
