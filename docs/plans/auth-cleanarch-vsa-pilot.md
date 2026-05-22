# Plan: Auth Service Clean Architecture + Vertical Slices Pilot

> Source PRD: [`docs/prd/PRD-Auth-CleanArch-VSA-Pilot.md`](../prd/PRD-Auth-CleanArch-VSA-Pilot.md) (GitHub issue [#183](https://github.com/daonhan/Microservices-in-.NET/issues/183))
> Companion ADR: [`docs/adr/0011-order-cleanarch-vsa-pilot.md`](../adr/0011-order-cleanarch-vsa-pilot.md) — composed by reference; no new ADR for Auth.
> Runbook: [`docs/runbooks/adding-a-new-slice.md`](../runbooks/adding-a-new-slice.md) — reused unchanged.
> Branch: `refactor/auth-cleanarch-vsa` — single PR for review.

## Context

`Auth.Service` is the fourth Clean Architecture + Vertical Slice (VSA) pilot in the repo (after Order, Product, Basket). Today it is organized by technical type: HTTP routes in `Endpoints/{AuthApiEndpoints, ServiceTokenEndpoint, JwksEndpoint}.cs`, DTOs in `ApiModels/LoginRequest.cs`, anemic data records in `Models/{User, AuthToken, ServiceClient}.cs`, token issuers in `Services/{JwtTokenService, ServiceTokenService}.cs`, signing helpers in `Services/Signing/*`, persistence in `Infrastructure/Data/EntityFramework/AuthContext.cs`. Reading "what happens on `POST /login`?" requires hopping across four folders, and the persistence layer leaks policy — `AuthContext.VerifyUserLogin` (lines 26–42) mixes EF data access with `IPasswordHasher<User>` verification and a dummy-hash timing-defense side channel.

This plan reorganizes `Auth.Service` into the same `Features/<Slice>/`, `Domain/`, `Infrastructure/` shape Order/Product/Basket use, inside a single `Auth.Service.csproj` (plus a sibling `Auth.Service.LayoutAnalyzer` analyzer sub-project). Auth diverges from Order/Product (no `Contracts/`, no outbox, no integration events) and aligns with Basket. The deepest module change is splitting `IAuthStore` from `VerifyUserLogin` to `FindByUsernameAsync`, moving password verification + dummy-hash timing defense into `LoginHandler`, and rewriting `JwtTokenService.GenerateAuthenticationToken` to take `User` instead of `(string username, string password)`. Everything else is relocation + namespace rename.

Zero behavior change on public surfaces. Every existing `Auth.Tests` test passes after namespace updates plus the small signature-driven adjustments documented in the PRD. `ECommerce.Shared` public API unchanged; no nupkg bump. No EF schema or migration changes. No changes to `/login`, `/token`, `/.well-known/jwks.json`, `/.well-known/openid-configuration` routes/status codes/response shapes/headers; metric counter names (`login-success`, `login-failure`, `service-token-success`, `service-token-failure`, `jwks-served`) preserved. Dummy-hash timing defense preserved verbatim.

Propagation to remaining services (inventory → payment → shipping → saga, plus api-gateway) is deferred to a follow-up ADR after Auth lands.

## Architectural decisions

Durable across all phases:

- **Project shape**: single `Auth.Service.csproj` retained. New sibling `Auth.Service.LayoutAnalyzer` analyzer sub-project (port-paste from `basket-microservice/Basket.Service.LayoutAnalyzer/`) referenced as `Analyzer` package reference. No `Auth.Domain`/`Auth.Application`/`Auth.Infrastructure` csproj split.
- **Folder topology**:
  - `Features/<Slice>/` — one folder per inbound HTTP trigger. Four slices: `Login`, `IssueServiceToken`, `GetJwks`, `GetOpenIdConfiguration`. Each owns its endpoint, request/response DTOs, sealed handler, slice DI extension.
  - `Domain/` — `User`, `AuthToken` records; `Domain/Abstractions/IAuthStore`, `Domain/Abstractions/IRsaKeyProvider` (`PublishedKey` record co-located); `Domain/Tokens/JwtTokenService`, `Domain/Tokens/ServiceTokenService`. No EF, no HTTP, no `System.IO` references.
  - `Infrastructure/Data/EntityFramework/` — `AuthContext` (persistence only), `EfAuthStore` (impl of `IAuthStore`), `AuthContextDatabaseMigration`, `Configurations/UserConfiguration`, `AuthDataExtensions.AddAuthDatastore` (renamed from `AddSqlServerDatastore`).
  - `Infrastructure/Signing/` — `PemFileRsaKeyProvider`, `SigningOptions`, `SigningInfrastructureExtensions.AddSigningInfrastructure`.
  - `Migrations/` — unchanged; `generated_code = true`.
  - **No `Contracts/` folder.** Auth produces and consumes no cross-service payloads. Documented divergence from Order/Product (matches Basket).
- **Namespaces** match folders: `Auth.Service.Domain`, `Auth.Service.Domain.Abstractions`, `Auth.Service.Domain.Tokens`, `Auth.Service.Features.Login`, `Auth.Service.Features.IssueServiceToken`, `Auth.Service.Features.GetJwks`, `Auth.Service.Features.GetOpenIdConfiguration`, `Auth.Service.Infrastructure.Data.EntityFramework`, `Auth.Service.Infrastructure.Signing`. Old `Auth.Service.Endpoints`, `Auth.Service.ApiModels`, `Auth.Service.Models`, `Auth.Service.Services`, `Auth.Service.Services.Signing`, `Auth.Service.Infrastructure.Data` namespaces removed as files relocate.
- **Dispatch model**: no MediatR, no in-house mediator. Endpoints take their slice handler class via `[FromServices]` (or constructor for class-style endpoints) and call `HandleAsync(...)` directly. Slice handler classes `internal sealed` with one public `(async)` method.
- **Token-builder placement**: `JwtTokenService` and `ServiceTokenService` move to `Domain/Tokens/`. `ServiceTokenService.GenerateServiceToken(clientId, clientSecret)` signature preserved. `JwtTokenService.GenerateAuthenticationToken` signature changes from `(string username, string password)` to `(User user)` — the password parameter is removed because verification is no longer the token-builder's concern. Claim set, expiry (15 min), signing algorithm (RS256), issuer derivation unchanged.
- **`IAuthStore` split**: `IAuthStore.VerifyUserLogin` is **removed**. Replaced by `IAuthStore.FindByUsernameAsync(string username)` returning `User?`. `AuthContext` no longer implements `IAuthStore`; new `EfAuthStore` class in `Infrastructure/Data/EntityFramework/` implements `FindByUsernameAsync`. `LoginHandler` calls `IAuthStore.FindByUsernameAsync(username)`, then runs `IPasswordHasher<User>.VerifyHashedPassword`. Dummy-hash timing-defense path moves into `LoginHandler` verbatim — same dummy hash constant, same call shape, same return-`null` behavior.
- **`IRsaKeyProvider` placement**: Domain abstraction in `Domain/Abstractions/`. `PemFileRsaKeyProvider` + `SigningOptions` move to `Infrastructure/Signing/`. `PublishedKey` record co-located with the interface.
- **`IPasswordHasher<User>`**: stays as the `Microsoft.AspNetCore.Identity` type. Not re-abstracted — `LoginHandler` already depends on `Microsoft.AspNetCore.Identity` and re-abstraction adds a wrapper without a second consumer.
- **`ServiceClientOptions` placement**: slice-local under `Features/IssueServiceToken/` because it has exactly one consumer. Rule-of-three applies: if a second slice needs it, extract to `Infrastructure/`.
- **Cross-slice sharing rule**: rule of three — duplicate freely; extract to `Domain/` (behavioral) or `Features/Shared/` (helper) only on the third use. NetArchTest forbids `Auth.Service.Features.<X>` referencing `Auth.Service.Features.<Y>` for any `X != Y`.
- **Boundary enforcement**:
  - NetArchTest rules in `Auth.Tests/Architecture/LayoutTests.cs` (ported from `Basket.Tests/Architecture/LayoutTests.cs` with namespace swap):
    1. `Auth.Service.Domain.*` may not reference `Auth.Service.Infrastructure.*` or `Auth.Service.Features.*`.
    2. `Auth.Service.Features.<X>.*` may not reference `Auth.Service.Features.<Y>.*` for any `X != Y`.
    3. `Auth.Service.Infrastructure.*` may not reference `Auth.Service.Features.*`.
    4. **Not applicable**: no `Contracts/` folder. Rule (4) from Order/Basket layout tests omitted; absence documented in the PR description.
  - Roslyn `Auth.Service.LayoutAnalyzer` sub-project (port-paste from `Basket.Service.LayoutAnalyzer`) raises the same three rules as compile-time errors.
  - Both guardrails must fail on an intentional cross-boundary spike before phase 8 is marked done. Spike-and-revert recorded in PR description.
- **Composition root as manifest**: `Program.cs` becomes a fluent chain — `builder.Services.AddAuthDatastore(...).AddSigningInfrastructure(...).AddLoginSlice(...).AddIssueServiceTokenSlice(...).AddGetJwksSlice().AddGetOpenIdConfigurationSlice()` plus the existing `AddPlatformObservability` / `AddPlatformHealthChecks` / `AddPlatformOpenApi` calls unchanged. `RegisterTokenService` is deleted (decomposed into `AddSigningInfrastructure` + slice extensions). The `app.UsePrometheusExporter() / MapPlatformHealthChecks / UsePlatformOpenApi`, `MigrateDatabase()`, `SeedQaData()` calls are unchanged; endpoint registration becomes `app.MapLogin(); app.MapIssueServiceToken(); app.MapGetJwks(); app.MapGetOpenIdConfiguration();`.
- **`AddSqlServerDatastore` rename**: Auth's local extension is named `AddSqlServerDatastore` (in `Auth.Service.Infrastructure.Data.EntityFramework`). Renamed to `AddAuthDatastore` per the PRD. Each microservice in the repo has its own same-named local extension; the rename gives Auth a service-specific name aligned with the new layout. Behavior unchanged.
- **Routes / contracts / payloads**: unchanged. `POST /login`, `POST /token`, `GET /.well-known/jwks.json`, `GET /.well-known/openid-configuration` keep identical status codes, response shapes, `Cache-Control: public, max-age=300` headers on discovery endpoints, RS256 signing, 15-minute token lifetimes, issuer derivation, and metric counter names (`login-success`, `login-failure`, `service-token-success`, `service-token-failure`, `jwks-served`).
- **QA seeder**: location unchanged. `AuthQaSeedTests` at `Auth.Tests/Qa/AuthQaSeedTests.cs` stays modulo namespace updates.
- **Dev keys / EF schema / migrations**: unchanged. No new EF migrations.
- **Shared library**: `ECommerce.Shared` public API unchanged. No nupkg version bump. Pilot composes existing `AddJwtAuthentication`, `AddPlatformObservability`, `AddPlatformHealthChecks`, `AddPlatformOpenApi`, `QaSeedingExtensions`.
- **Validation / `Auth.Service.csproj` split / MediatR / domain events / outbox**: out of scope. Per-slice request validation deferred.
- **Test layout**: `Auth.Tests/Features/<Slice>/` mirrors `Features/<Slice>/`. Domain unit tests stay in `Auth.Tests/Domain/Tokens/` (`JwtTokenServiceTests`, `ServiceTokenServiceTests`). New `Auth.Tests/Architecture/LayoutTests.cs`. New `Auth.Tests/Infrastructure/Data/EntityFramework/EfAuthStoreTests.cs` for `FindByUsernameAsync` (replaces `AuthContextVerifyUserLoginTests`). `Auth.Tests/Qa/AuthQaSeedTests.cs` stays.
- **Commit gating**: pre-commit hook (`dotnet husky run --group pre-commit` — runs `dotnet format --verify-no-changes`, `dotnet build --no-restore`, and **Basket tests only**) gates every commit. Auth tests are run manually before pushing per the root `CLAUDE.md` sandbox policy. No `--no-verify`. No `Hooks-Deferred:` / `Validation-Deferred:` footers. If the sandbox hook cannot pass, stop and hand off to host.

---

## Phase 1: Scaffold — NetArchTest dependency + LayoutAnalyzer sub-project + skipped layout tests

**User stories**: 10, 11, 14, 22

### What to build

Lay enforcement scaffolding so later phases can flip rules on without re-authoring. Add a `NetArchTest.Rules` package reference to `Auth.Tests`. Create `Auth.Tests/Architecture/LayoutTests.cs` with the three applicable boundary rules authored but **skipped** (`[Fact(Skip = "Enabled in phase 8")]`). Scaffold a new `Auth.Service.LayoutAnalyzer` sub-project — port-paste from `basket-microservice/Basket.Service.LayoutAnalyzer/LayoutAnalyzer.cs` with the namespace prefix swap `Basket` → `Auth` and the analyzed-namespace prefix swap `Basket.Service.*` → `Auth.Service.*`. Wire it into `Auth.Service.csproj` as an `Analyzer` package reference but keep its rules at warning-only severity in `.editorconfig` for this phase. Update `auth-microservice.slnx` to include the new analyzer project. No source-file moves in this phase; only the test project and analyzer project change.

### Acceptance criteria

- [ ] `Auth.Tests/Architecture/LayoutTests.cs` exists with three NetArchTest rules authored as `[Fact(Skip = "Enabled in phase 8")]`.
- [ ] `auth-microservice/Auth.Service.LayoutAnalyzer/` sub-project exists with `LayoutAnalyzer.cs` ported from `Basket.Service.LayoutAnalyzer` (namespace `Auth.Service.LayoutAnalyzer`, analyzed prefix `Auth.Service.*`).
- [ ] `Auth.Service.csproj` references `Auth.Service.LayoutAnalyzer` as an `Analyzer` package reference.
- [ ] `auth-microservice.slnx` includes the new analyzer project.
- [ ] `.editorconfig` declares the analyzer's rules at warning-only severity for this phase.
- [ ] `dotnet build` clean for the whole solution; no new errors or warnings.
- [ ] `dotnet test` green (the three skipped tests reported as skipped, not failed).
- [ ] Pre-commit hook (`dotnet husky run --group pre-commit`) passes on the commit.

---

## Phase 2: Layout move — Domain records (`User`, `AuthToken`)

**User stories**: 4, 14, 17

### What to build

Move pure-domain data records into the new layout without changing behavior. Create `Domain/`. Relocate `Models/User.cs` and `Models/AuthToken.cs` into `Domain/`. Rename namespaces to `Auth.Service.Domain`. Update all `using` directives across `Auth.Service` and `Auth.Tests` to point at the new namespace. `Models/ServiceClient.cs` does **not** move yet (it is co-owned by `ServiceClientOptions`, which moves with the `IssueServiceToken` slice in phase 6). `AuthContext`, `JwtTokenService`, `ServiceTokenService`, `IAuthStore`, the endpoints, and the tests update their `using` directives but do not move.

### Acceptance criteria

- [ ] `Domain/User.cs`, `Domain/AuthToken.cs` exist with namespace `Auth.Service.Domain`.
- [ ] No file in `Domain/` has a `using` for `Microsoft.EntityFrameworkCore`, `Microsoft.AspNetCore.*`, or any `Auth.Service.Infrastructure.*`/`Auth.Service.Services.*`/`Auth.Service.Endpoints.*` namespace.
- [ ] `Models/User.cs` and `Models/AuthToken.cs` removed; `Models/ServiceClient.cs` still in `Models/`.
- [ ] `dotnet build` clean across the repo.
- [ ] `dotnet test` green — every existing `Auth.Tests` test passes after namespace updates only.
- [ ] Pre-commit hook passes on the commit.

---

## Phase 3: `IAuthStore` split — `FindByUsernameAsync` + `EfAuthStore`; LoginHandler owns verification

**User stories**: 5, 6, 12, 17

### What to build

The deepest module change in the pilot. Three coupled edits land in one commit:

1. **Replace `IAuthStore`**. Delete the current `Auth.Service.Infrastructure.Data.IAuthStore` interface. Create `Domain/Abstractions/IAuthStore` (namespace `Auth.Service.Domain.Abstractions`) declaring a single `Task<User?> FindByUsernameAsync(string username)` method. `AuthContext` no longer implements `IAuthStore` — remove the interface from its class declaration and delete the `VerifyUserLogin` method body, the `IPasswordHasher<User>` constructor parameter, the `DummyHash` constant, and the `using Microsoft.AspNetCore.Identity` import (this strips `AuthContext` to a pure `DbContext`).
2. **Add `EfAuthStore`**. New class `Infrastructure/Data/EntityFramework/EfAuthStore.cs` (namespace `Auth.Service.Infrastructure.Data.EntityFramework`) implementing `IAuthStore.FindByUsernameAsync` via `AuthContext.Users.FirstOrDefaultAsync(u => u.Username == username)`. Register as `services.AddScoped<IAuthStore, EfAuthStore>()` in the existing `EntityFrameworkExtensions.AddSqlServerDatastore` (kept under that name until phase 5; renamed there).
3. **Move verification into `LoginHandler`**. Today `JwtTokenService.GenerateAuthenticationToken(username, password)` calls `IAuthStore.VerifyUserLogin`. Introduce an interim `LoginHandler` in `Services/` (kept here for one commit; moves to `Features/Login/` in phase 6). `LoginHandler` constructor takes `IAuthStore` + `IPasswordHasher<User>` + `JwtTokenService`. Its public method: call `FindByUsernameAsync(username)`; if `null`, invoke `_hasher.VerifyHashedPassword(new User { Username = "", PasswordHash = DummyHash, Role = "" }, DummyHash, password)` with the exact same dummy-hash constant (copied verbatim from `AuthContext` — `"AQAAAAIAAYag..."`), return `null`. Otherwise call `_hasher.VerifyHashedPassword(user, user.PasswordHash, password)`; on `Success`/`SuccessRehashNeeded` call `JwtTokenService.GenerateAuthenticationToken(user)` (signature changed to `(User)` — see step below) and return its `AuthToken`; else return `null`. `JwtTokenService.GenerateAuthenticationToken` signature changes from `(string, string)` to `(User)`: drop the `IAuthStore.VerifyUserLogin` call inside it and have it accept the already-verified `User`. The claim set, RS256 signing, 15-min expiry, issuer derivation are unchanged. `AuthApiEndpoints` is rewired to inject `LoginHandler` instead of `ITokenService` and to call `LoginHandler.HandleAsync(username, password)`.

`AuthContextVerifyUserLoginTests` is **renamed and re-pointed** to `Auth.Tests/Infrastructure/Data/EntityFramework/EfAuthStoreTests.cs` covering: returns `null` for unknown username; returns the entity for a seeded user. `JwtTokenServiceTests` is updated to call the new `(User)` signature; the assertion surface (claims, expiry, signing) is unchanged. New `Auth.Tests/LoginHandlerTests.cs` (placed at root for this phase; moves with the slice in phase 7) covers: unknown username → `null` and dummy-hash path executed; valid username + wrong password → `null`; valid credentials → `AuthToken`. Uses `NSubstitute` on `IAuthStore` and a real `PasswordHasher<User>`.

### Acceptance criteria

- [ ] `Domain/Abstractions/IAuthStore.cs` exists with `Task<User?> FindByUsernameAsync(string username)` and namespace `Auth.Service.Domain.Abstractions`.
- [ ] Old `Infrastructure/Data/IAuthStore.cs` deleted.
- [ ] `AuthContext` no longer implements `IAuthStore`. `VerifyUserLogin`, `_hasher`, `DummyHash`, and the `Microsoft.AspNetCore.Identity` `using` are removed from `AuthContext.cs`. `AuthContext` constructor no longer accepts `IPasswordHasher<User>`.
- [ ] `Infrastructure/Data/EntityFramework/EfAuthStore.cs` exists and implements `IAuthStore.FindByUsernameAsync`. `IAuthStore` is registered against `EfAuthStore` (scoped).
- [ ] `LoginHandler` exists (in `Services/` for this phase; moves in phase 6) and orchestrates `IAuthStore` + `IPasswordHasher<User>` + `JwtTokenService`. The dummy-hash constant matches `AuthContext`'s previous value byte-for-byte.
- [ ] `JwtTokenService.GenerateAuthenticationToken` signature is `Task<AuthToken?> GenerateAuthenticationToken(User user)` (or sync return type if previously sync — match the existing return contract). It no longer references `IAuthStore`.
- [ ] `AuthApiEndpoints.cs` injects `LoginHandler` (not `ITokenService`) and calls its handler method. The `/login` route, request DTO, response shape, status codes, and `login-success` / `login-failure` metric counters are unchanged.
- [ ] `Auth.Tests/AuthContextVerifyUserLoginTests.cs` deleted and replaced by `Auth.Tests/Infrastructure/Data/EntityFramework/EfAuthStoreTests.cs` (covers `null` + entity-returned paths).
- [ ] `Auth.Tests/LoginHandlerTests.cs` exists and covers unknown-username (with dummy-hash invocation asserted), wrong-password, and success paths.
- [ ] `Auth.Tests/JwtTokenServiceTests.cs` updated to the `(User)` signature with assertion surface unchanged.
- [ ] `dotnet build` clean across the repo.
- [ ] `dotnet test` green; `AuthApiEndpointsTests` still passes (login behavior identical).
- [ ] Manual smoke: `POST /login` with valid credentials returns `AuthToken`; invalid credentials return `401`; metric counters increment as before.
- [ ] Pre-commit hook passes on the commit.

---

## Phase 4: Layout move — `JwtTokenService`, `ServiceTokenService` → `Domain/Tokens/`

**User stories**: 4, 14, 17, 19

### What to build

Relocate the two token-builder domain services. Create `Domain/Tokens/`. Move `Services/JwtTokenService.cs` and `Services/ServiceTokenService.cs` into `Domain/Tokens/` with namespace `Auth.Service.Domain.Tokens`. `ITokenService` and `IServiceTokenService` move with them (kept for now; the slice extraction in phase 6 may eliminate the interfaces if they are single-use, but defer that decision). `ServiceTokenService.GenerateServiceToken(clientId, clientSecret)` signature unchanged. `JwtTokenService.GenerateAuthenticationToken(User)` signature already established in phase 3; only its location and namespace change here. All consumers (`LoginHandler`, the `ServiceTokenEndpoint`, `TokenStartupExtensions.RegisterTokenService`) update their `using` directives. `IRsaKeyProvider` stays at its current location for this phase (moves in phase 5). `ServiceClientOptions` and `Models/ServiceClient.cs` stay in place for this phase (move with the slice in phase 6).

### Acceptance criteria

- [ ] `Domain/Tokens/JwtTokenService.cs`, `Domain/Tokens/ServiceTokenService.cs` exist with namespace `Auth.Service.Domain.Tokens`. `ITokenService.cs` and `IServiceTokenService.cs` move with them (same namespace).
- [ ] Old files under `Services/` (`JwtTokenService.cs`, `ServiceTokenService.cs`, `ITokenService.cs`, `IServiceTokenService.cs`) removed.
- [ ] No file in `Domain/Tokens/` references any `Auth.Service.Infrastructure.*`, `Auth.Service.Features.*`, or `Auth.Service.Endpoints.*` namespace.
- [ ] `Auth.Tests/JwtTokenServiceTests.cs` and `Auth.Tests/ServiceTokenServiceTests.cs` updated `using` directives only; assertion surface unchanged.
- [ ] `dotnet build` clean across the repo.
- [ ] `dotnet test` green.
- [ ] Pre-commit hook passes on the commit.

---

## Phase 5: Layout move — `IRsaKeyProvider` → Domain; signing infra → `Infrastructure/Signing/`; `AddSigningInfrastructure`; rename `AddAuthDatastore`

**User stories**: 9, 14, 17

### What to build

Finish the Domain/Infrastructure split for signing concerns and rename the datastore extension. Move `Services/Signing/IRsaKeyProvider.cs` (interface + co-located `PublishedKey` record) to `Domain/Abstractions/IRsaKeyProvider.cs` with namespace `Auth.Service.Domain.Abstractions`. Move `Services/Signing/PemFileRsaKeyProvider.cs` and `Services/Signing/SigningOptions.cs` to `Infrastructure/Signing/` with namespace `Auth.Service.Infrastructure.Signing`. Create `Infrastructure/Signing/SigningInfrastructureExtensions.cs` declaring `public static IServiceCollection AddSigningInfrastructure(this IServiceCollection services, IConfiguration configuration)` that registers `SigningOptions` (bound from configuration) and `IRsaKeyProvider` against `PemFileRsaKeyProvider` (singleton, matching today's lifetime). Rename `Auth.Service.Infrastructure.Data.EntityFramework.EntityFrameworkExtensions.AddSqlServerDatastore` to `AddAuthDatastore`, returning `IServiceCollection` for chaining (matches the slice-extension pattern). Update `Program.cs` to call `.AddAuthDatastore(builder.Configuration).AddSigningInfrastructure(builder.Configuration)` immediately after `AddSingleton<IPasswordHasher<User>, PasswordHasher<User>>()`. The existing `TokenStartupExtensions.RegisterTokenService` is **partially gutted** here: the signing registrations and the `SigningOptions` binding move into `AddSigningInfrastructure`; the `AuthOptions` binding stays in `RegisterTokenService` (deleted in phase 6 with slice extraction). `JwtTokenService` and `ServiceTokenService` registrations stay in `RegisterTokenService` for this phase (move into slice extensions in phase 6). The `Services/Signing/` folder is deleted once empty.

### Acceptance criteria

- [ ] `Domain/Abstractions/IRsaKeyProvider.cs` (with `PublishedKey` record) exists with namespace `Auth.Service.Domain.Abstractions`.
- [ ] `Infrastructure/Signing/PemFileRsaKeyProvider.cs`, `Infrastructure/Signing/SigningOptions.cs`, `Infrastructure/Signing/SigningInfrastructureExtensions.cs` exist with namespace `Auth.Service.Infrastructure.Signing`.
- [ ] `Services/Signing/` folder deleted.
- [ ] `AddAuthDatastore` exists (renamed from `AddSqlServerDatastore`), returns `IServiceCollection` for chaining, and is called from `Program.cs`. No remaining call to `AddSqlServerDatastore` in Auth.
- [ ] `AddSigningInfrastructure(IConfiguration)` exists, binds `SigningOptions` from configuration, registers `IRsaKeyProvider` against `PemFileRsaKeyProvider` (singleton). Called from `Program.cs` immediately after `AddAuthDatastore`.
- [ ] `TokenStartupExtensions.RegisterTokenService` no longer registers `SigningOptions` / `IRsaKeyProvider` / `PemFileRsaKeyProvider`. Still registers `JwtTokenService`, `ServiceTokenService`, `AuthOptions`, `ServiceClientOptions` for this phase.
- [ ] `dotnet build` clean across the repo.
- [ ] `dotnet test` green — all existing tests pass; the JWKS endpoint test continues to observe the same `Cache-Control` header and `jwks-served` counter.
- [ ] Manual smoke: `GET /.well-known/jwks.json` returns the same JSON shape and headers; `POST /login` and `POST /token` issue valid RS256 tokens against the same dev key.
- [ ] Pre-commit hook passes on the commit.

---

## Phase 6: Extract HTTP slices — `Login`, `IssueServiceToken`, `GetJwks`, `GetOpenIdConfiguration`

**User stories**: 1, 2, 3, 6, 7, 8, 17, 18, 19

### What to build

Extract the four HTTP routes into self-contained vertical slices. For each slice, create `Features/<Slice>/` containing: the request/response DTOs (where applicable), a sealed slice handler class with one public method, the route registration (an `IEndpointRouteBuilder` extension `MapXxx`), and an `AddXxxSlice(this IServiceCollection)` extension that registers the handler plus any slice-local options. The four slices may land as one commit each or as a single bundled commit — acceptance criteria are per-slice so partial progress is trackable.

#### Login (`POST /login`)

`Features/Login/` contains: `LoginRequest` (moved from `ApiModels/LoginRequest.cs`, namespace `Auth.Service.Features.Login`), `LoginEndpoint.cs` exposing `MapLogin(this IEndpointRouteBuilder)` registering `POST /login` with `.AllowAnonymous()` (current posture), `LoginHandler.cs` (moved from `Services/`, kept internal sealed, namespace `Auth.Service.Features.Login`) — composes `IAuthStore` + `IPasswordHasher<User>` + `JwtTokenService` + the dummy-hash timing defense exactly as in phase 3, and `LoginSliceExtensions.cs` with `AddLoginSlice(this IServiceCollection, IConfiguration)` registering `LoginHandler` (scoped) and binding `AuthOptions` from configuration (moved out of `TokenStartupExtensions`). The endpoint method delegates to `LoginHandler.HandleAsync(request.Username, request.Password)`. Metric counters `login-success` / `login-failure` are incremented inside `LoginHandler` (where they live today via `MetricFactory` in `AuthApiEndpoints` — moved into the handler as part of slice ownership).

#### IssueServiceToken (`POST /token`)

`Features/IssueServiceToken/` contains: `IssueServiceTokenRequest` (the `client_credentials` form-bound model), `IssueServiceTokenEndpoint.cs` exposing `MapIssueServiceToken(this IEndpointRouteBuilder)` registering `POST /token` with the same auth posture as today, `IssueServiceTokenHandler.cs` (internal sealed, namespace `Auth.Service.Features.IssueServiceToken`) — composes `ServiceClientOptions` lookup + `ServiceTokenService.GenerateServiceToken(clientId, clientSecret)`, `ServiceClientOptions.cs` and `ServiceClient.cs` (moved here from `Models/ServiceClient.cs` and `TokenStartupExtensions`, slice-local per the rule-of-three), and `IssueServiceTokenSliceExtensions.cs` with `AddIssueServiceTokenSlice(this IServiceCollection, IConfiguration)` registering `IssueServiceTokenHandler` and binding `ServiceClientOptions`. Metric counters `service-token-success` / `service-token-failure` move into the handler.

#### GetJwks (`GET /.well-known/jwks.json`)

`Features/GetJwks/` contains: `Jwk` and `JwksDocument` response records (moved from `Endpoints/JwksEndpoint.cs`, namespace `Auth.Service.Features.GetJwks`), `GetJwksEndpoint.cs` exposing `MapGetJwks(this IEndpointRouteBuilder)` registering `GET /.well-known/jwks.json` with `.AllowAnonymous()`, `GetJwksHandler.cs` (internal sealed) — composes `IRsaKeyProvider.GetPublishedPublicKeys()`, builds `Jwk` records via `Base64UrlEncoder.Encode`, increments the `jwks-served` counter, and sets the `Cache-Control: public, max-age=300` response header. `GetJwksSliceExtensions.AddGetJwksSlice(this IServiceCollection)` registers `GetJwksHandler`.

#### GetOpenIdConfiguration (`GET /.well-known/openid-configuration`)

`Features/GetOpenIdConfiguration/` contains: `OpenIdConfigurationDocument` response record (moved from `Endpoints/JwksEndpoint.cs`, namespace `Auth.Service.Features.GetOpenIdConfiguration`), `GetOpenIdConfigurationEndpoint.cs` exposing `MapGetOpenIdConfiguration(this IEndpointRouteBuilder)` registering `GET /.well-known/openid-configuration` with `.AllowAnonymous()`, `GetOpenIdConfigurationHandler.cs` (internal sealed) — derives `issuer` from `httpContext.Request.Scheme` + `Host`, builds the response with `id_token_signing_alg_values_supported: [RS256]`, sets `Cache-Control: public, max-age=300`. `GetOpenIdConfigurationSliceExtensions.AddGetOpenIdConfigurationSlice(this IServiceCollection)` registers the handler.

#### Composition root + cleanup

`Program.cs` becomes a manifest:

```
builder.Services.AddAuthDatastore(builder.Configuration)
                .AddSigningInfrastructure(builder.Configuration)
                .AddLoginSlice(builder.Configuration)
                .AddIssueServiceTokenSlice(builder.Configuration)
                .AddGetJwksSlice()
                .AddGetOpenIdConfigurationSlice();
// AddSingleton<IPasswordHasher<User>, PasswordHasher<User>>() stays — consumed by LoginHandler
// AddPlatformObservability / AddPlatformHealthChecks / AddPlatformOpenApi unchanged
var app = builder.Build();
// UsePrometheusExporter / MapPlatformHealthChecks / UsePlatformOpenApi / MigrateDatabase / SeedQaData unchanged
app.MapLogin();
app.MapIssueServiceToken();
app.MapGetJwks();
app.MapGetOpenIdConfiguration();
```

`Services/TokenStartupExtensions.cs` and `RegisterTokenService` deleted (decomposed into `AddSigningInfrastructure` + slice extensions). `Endpoints/AuthApiEndpoints.cs`, `Endpoints/ServiceTokenEndpoint.cs`, `Endpoints/JwksEndpoint.cs` deleted; `Endpoints/` folder removed. `ApiModels/LoginRequest.cs` removed (moved into slice); `ApiModels/` folder removed if empty. `Models/ServiceClient.cs` removed (moved into slice); `Models/` folder removed if empty. `Services/` folder removed if empty.

### Acceptance criteria

#### Login
- [ ] `Features/Login/` contains `LoginRequest`, `LoginEndpoint`, `LoginHandler`, `LoginSliceExtensions` with namespace `Auth.Service.Features.Login`.
- [ ] `Program.cs` chains `.AddLoginSlice(builder.Configuration)` and calls `app.MapLogin()`. Route `/login`, status codes, response shape, and `login-success` / `login-failure` counters preserved byte-identically.
- [ ] Dummy-hash timing-defense constant and call shape in `LoginHandler` byte-identical to the value used in `AuthContext` before phase 3.

#### IssueServiceToken
- [ ] `Features/IssueServiceToken/` contains `IssueServiceTokenEndpoint`, `IssueServiceTokenHandler`, `ServiceClientOptions`, `ServiceClient`, `IssueServiceTokenSliceExtensions` with namespace `Auth.Service.Features.IssueServiceToken`.
- [ ] `Program.cs` chains `.AddIssueServiceTokenSlice(builder.Configuration)` and calls `app.MapIssueServiceToken()`. Route `/token`, status codes, response shape, and `service-token-success` / `service-token-failure` counters preserved byte-identically.

#### GetJwks
- [ ] `Features/GetJwks/` contains `GetJwksEndpoint`, `GetJwksHandler`, `Jwk`, `JwksDocument`, `GetJwksSliceExtensions` with namespace `Auth.Service.Features.GetJwks`.
- [ ] `Program.cs` chains `.AddGetJwksSlice()` and calls `app.MapGetJwks()`. Route `/.well-known/jwks.json`, JSON shape, `Cache-Control: public, max-age=300` header, and `jwks-served` counter preserved byte-identically.

#### GetOpenIdConfiguration
- [ ] `Features/GetOpenIdConfiguration/` contains `GetOpenIdConfigurationEndpoint`, `GetOpenIdConfigurationHandler`, `OpenIdConfigurationDocument`, `GetOpenIdConfigurationSliceExtensions` with namespace `Auth.Service.Features.GetOpenIdConfiguration`.
- [ ] `Program.cs` chains `.AddGetOpenIdConfigurationSlice()` and calls `app.MapGetOpenIdConfiguration()`. Route `/.well-known/openid-configuration`, JSON shape, and `Cache-Control: public, max-age=300` header preserved byte-identically. Issuer derivation from `Scheme` + `Host` and `RS256` algorithm advertised unchanged.

#### Phase-wide
- [ ] `Services/TokenStartupExtensions.cs` and `RegisterTokenService` deleted.
- [ ] `Endpoints/`, `ApiModels/`, `Models/`, `Services/` folders removed (or any remaining file justified in the PR description).
- [ ] No file in `Features/<X>/` references any `Auth.Service.Features.<Y>.*` namespace for any other slice.
- [ ] `dotnet build` clean across the repo.
- [ ] `dotnet test` green — all existing endpoint tests pass after namespace updates; `AuthApiEndpointsTests`, `ServiceTokenEndpointTests`, `JwksEndpointTests` continue to assert the same routes/shapes/headers/counters.
- [ ] Manual smoke against running service confirms all four routes return the same status codes, response shapes, headers, and observability signals as before.
- [ ] Pre-commit hook passes on each commit.

---

## Phase 7: Test reshape — `Auth.Tests/Features/<Slice>/` and `Auth.Tests/Domain/Tokens/`

**User stories**: 15, 16, 17

### What to build

Reshape `Auth.Tests` to mirror the production layout while keeping the cross-cutting test files at the project root or in dedicated cross-cutting folders. Move `Auth.Tests/JwtTokenServiceTests.cs` and `Auth.Tests/ServiceTokenServiceTests.cs` to `Auth.Tests/Domain/Tokens/`. Split the endpoint tests so each slice owns its endpoint tests: `Auth.Tests/Features/Login/LoginEndpointTests.cs` (split out from `AuthApiEndpointsTests.cs`), `Auth.Tests/Features/Login/LoginHandlerTests.cs` (the unit tests created in phase 3, relocated from `Auth.Tests/` root), `Auth.Tests/Features/IssueServiceToken/IssueServiceTokenEndpointTests.cs` (rename of `ServiceTokenEndpointTests.cs`), `Auth.Tests/Features/IssueServiceToken/IssueServiceTokenHandlerTests.cs` (new unit tests covering unknown `client_id`, wrong `client_secret`, valid → `AuthToken`), `Auth.Tests/Features/GetJwks/GetJwksEndpointTests.cs` (split out from `JwksEndpointTests.cs` for the `/jwks.json` assertions), `Auth.Tests/Features/GetOpenIdConfiguration/GetOpenIdConfigurationEndpointTests.cs` (split out for the `/.well-known/openid-configuration` assertions). Endpoint tests continue to use `WebApplicationFactory<Program>` against the existing `public partial class Program { }`. `Auth.Tests/Infrastructure/Data/EntityFramework/EfAuthStoreTests.cs` (created in phase 3) stays. `Auth.Tests/Architecture/LayoutTests.cs` (from phase 1) stays with rules still skipped. `Auth.Tests/Qa/AuthQaSeedTests.cs` stays modulo namespace updates. Delete the now-empty `Auth.Tests/` root test files (`AuthApiEndpointsTests.cs`, `JwksEndpointTests.cs`, etc.) once their contents have been split.

### Acceptance criteria

- [ ] `Auth.Tests/Features/Login/`, `Auth.Tests/Features/IssueServiceToken/`, `Auth.Tests/Features/GetJwks/`, `Auth.Tests/Features/GetOpenIdConfiguration/` each contain the endpoint + handler tests for their slice.
- [ ] `Auth.Tests/Domain/Tokens/JwtTokenServiceTests.cs` and `Auth.Tests/Domain/Tokens/ServiceTokenServiceTests.cs` exist with namespace `Auth.Tests.Domain.Tokens`.
- [ ] `Auth.Tests/Infrastructure/Data/EntityFramework/EfAuthStoreTests.cs` exists (carried from phase 3).
- [ ] `Auth.Tests/Architecture/LayoutTests.cs` exists with rules still skipped.
- [ ] `Auth.Tests/Qa/AuthQaSeedTests.cs` exists with updated namespace.
- [ ] Old root-level test files (`AuthApiEndpointsTests.cs`, `JwksEndpointTests.cs`, `ServiceTokenEndpointTests.cs`, `JwtTokenServiceTests.cs`, `ServiceTokenServiceTests.cs`, `LoginHandlerTests.cs`) are deleted; their contents now live under `Features/`, `Domain/Tokens/`, or `Infrastructure/`.
- [ ] `dotnet test` green; test count before this phase equals test count after (no tests dropped or duplicated beyond the new EfAuthStore + handler tests added in phase 3 and the new IssueServiceTokenHandler tests added here).
- [ ] Pre-commit hook passes on the commit.

---

## Phase 8: Enforcement — unskip NetArchTest rules + analyzer as errors + spike-and-revert

**User stories**: 10, 11, 13, 24

### What to build

Turn enforcement on. Remove the `[Fact(Skip = ...)]` attributes from every rule in `Auth.Tests/Architecture/LayoutTests.cs`. Promote the `Auth.Service.LayoutAnalyzer` rules in `.editorconfig` from warning-only to error severity for each of the three boundary rules:

- Code in `Auth.Service.Domain.*` may not reference `Auth.Service.Infrastructure.*` or `Auth.Service.Features.*`.
- Code in `Auth.Service.Features.<X>.*` may not reference `Auth.Service.Features.<Y>.*` for any `X != Y`.
- Code in `Auth.Service.Infrastructure.*` may not reference `Auth.Service.Features.*`.

(Rule 4 from Basket — `Contracts.*` may not reference internal `*` — is omitted; Auth has no `Contracts/` folder. Absence documented in the PR description.)

Demonstrate that both guardrails fire on an intentional violation: introduce one cross-boundary `using` in a throwaway commit (e.g. `using Auth.Service.Infrastructure.Data.EntityFramework;` inside `Domain/Tokens/JwtTokenService.cs`, or `using Auth.Service.Features.Login;` inside `Features/IssueServiceToken/`). Confirm NetArchTest fails AND the `Auth.Service.LayoutAnalyzer` raises a build-time error. Revert the spike before the phase merges. Document the spike-and-revert demonstration in the PR description (linked commit shas + paste of both error outputs).

### Acceptance criteria

- [ ] No `[Fact(Skip = ...)]` remains in `Auth.Tests/Architecture/LayoutTests.cs`. All three layout tests run and pass.
- [ ] `Auth.Service/.editorconfig` (or equivalent analyzer config) declares the three `Auth.Service.LayoutAnalyzer` rules at error severity.
- [ ] PR description records the spike-and-revert demonstration showing both NetArchTest and the analyzer fire on a deliberately introduced cross-boundary reference. Both error messages are quoted in the PR description.
- [ ] PR description records the omission of the `Contracts.*` rule with the justification "Auth has no `Contracts/` folder; rule (4) from Basket is not applicable."
- [ ] `dotnet build` clean across the repo.
- [ ] `dotnet test` green across the repo.
- [ ] Pre-commit hook passes on the commit.

---

## Phase 9: Docs — root `CLAUDE.md` Auth pilot line

**User stories**: 22, 23

### What to build

Update root `CLAUDE.md` to record Auth as the fourth Clean Architecture + VSA pilot. Add an "Auth service exception" paragraph mirroring the existing "Order service exception" / "Product service exception" / "Basket service exception" entries: name the layout, point at NetArchTest (`Auth.Tests/Architecture/LayoutTests.cs`) and the Roslyn `Auth.Service.LayoutAnalyzer`, compose ADR 0011 by reference, reuse the `adding-a-new-slice.md` runbook, and document divergences from Order/Product (no `Contracts/`, no outbox seam, no integration events — same divergence shape as Basket, expanded to also note no `Contracts/`). If `auth-microservice/CLAUDE.md` exists, add the same pointer there. No new ADR. No new runbook. No update to `docs/adr/0011-order-cleanarch-vsa-pilot.md`'s follow-up list — the PRD's "Further Notes" captures the revised propagation order (inventory → payment → shipping → saga).

### Acceptance criteria

- [ ] Root `CLAUDE.md` has an "Auth service exception" paragraph referencing ADR 0011, the runbook, NetArchTest, and the Roslyn analyzer, plus the documented divergences (no `Contracts/`, no outbox, no integration events).
- [ ] `auth-microservice/CLAUDE.md` mentions both ADR + runbook (if such a file exists in the repo).
- [ ] `dotnet build` clean and `dotnet test` green across the repo.
- [ ] Pre-commit hook passes on the commit.

---

## Verification (end-to-end)

After all phases land on `refactor/auth-cleanarch-vsa`:

1. `cd auth-microservice && dotnet build` — clean across `Auth.Service`, `Auth.Service.LayoutAnalyzer`, `Auth.Tests`.
2. `cd auth-microservice && dotnet test` — all unit + endpoint + architecture tests green. Test count matches pre-pilot plus the new `EfAuthStoreTests`, `LoginHandlerTests`, `IssueServiceTokenHandlerTests`, and three architecture tests.
3. `docker compose up --build auth` — service boots, `/health` returns Healthy, `/metrics` exposes the same Prometheus counters (`login-success`, `login-failure`, `service-token-success`, `service-token-failure`, `jwks-served`), OpenAPI document renders.
4. Manual smoke via Bruno/curl:
   - `POST /login` with QA-seeded credentials → 200 + `AuthToken`; invalid credentials → 401; metric counters increment.
   - `POST /token` with valid `client_credentials` form → 200 + `AuthToken`; invalid `client_id` → 401; metric counters increment.
   - `GET /.well-known/jwks.json` → 200 + JWKS JSON + `Cache-Control: public, max-age=300` + `jwks-served` counter increments.
   - `GET /.well-known/openid-configuration` → 200 + document with `issuer`, `jwks_uri`, `id_token_signing_alg_values_supported: ["RS256"]` + `Cache-Control: public, max-age=300`.
   - A token issued by `POST /login` validates successfully when presented to any downstream service that uses `ECommerce.Shared.AddJwtAuthentication` (e.g. Order, Product, Basket).
5. Pre-commit hook (`dotnet husky run --group pre-commit`) passes on the merge commit.
6. PR description includes: spike-and-revert demonstration (phase 8), rationale for omitting NetArchTest rule (4), justification for the `AddSqlServerDatastore` → `AddAuthDatastore` rename, list of preserved public surfaces (routes, status codes, response shapes, headers, metric counter names).

## Critical files

Auth.Service today (will move or change):

- `auth-microservice/Auth.Service/Program.cs` — composition root (phases 3, 5, 6).
- `auth-microservice/Auth.Service/Infrastructure/Data/EntityFramework/AuthContext.cs` — `VerifyUserLogin` deletion (phase 3).
- `auth-microservice/Auth.Service/Infrastructure/Data/IAuthStore.cs` — interface replaced (phase 3).
- `auth-microservice/Auth.Service/Infrastructure/Data/EntityFramework/EntityFrameworkExtensions.cs` — rename + chaining (phase 5).
- `auth-microservice/Auth.Service/Services/JwtTokenService.cs` — signature change (phase 3) + move (phase 4).
- `auth-microservice/Auth.Service/Services/ServiceTokenService.cs` — move (phase 4).
- `auth-microservice/Auth.Service/Services/TokenStartupExtensions.cs` — decomposed + deleted (phases 5–6).
- `auth-microservice/Auth.Service/Services/Signing/IRsaKeyProvider.cs` — move to `Domain/Abstractions/` (phase 5).
- `auth-microservice/Auth.Service/Services/Signing/PemFileRsaKeyProvider.cs` — move to `Infrastructure/Signing/` (phase 5).
- `auth-microservice/Auth.Service/Services/Signing/SigningOptions.cs` — move to `Infrastructure/Signing/` (phase 5).
- `auth-microservice/Auth.Service/Endpoints/AuthApiEndpoints.cs` — replaced by `Features/Login/` (phase 6).
- `auth-microservice/Auth.Service/Endpoints/ServiceTokenEndpoint.cs` — replaced by `Features/IssueServiceToken/` (phase 6).
- `auth-microservice/Auth.Service/Endpoints/JwksEndpoint.cs` — replaced by `Features/GetJwks/` + `Features/GetOpenIdConfiguration/` (phase 6).
- `auth-microservice/Auth.Service/ApiModels/LoginRequest.cs` — move to `Features/Login/` (phase 6).
- `auth-microservice/Auth.Service/Models/{User,AuthToken}.cs` — move to `Domain/` (phase 2).
- `auth-microservice/Auth.Service/Models/ServiceClient.cs` — move to `Features/IssueServiceToken/` (phase 6).
- `auth-microservice/Auth.Tests/AuthContextVerifyUserLoginTests.cs` — replaced by `EfAuthStoreTests` (phase 3).
- `auth-microservice/Auth.Tests/{AuthApiEndpoints,ServiceTokenEndpoint,Jwks}Tests.cs` — split per slice (phase 7).
- `auth-microservice/Auth.Tests/{JwtTokenService,ServiceTokenService}Tests.cs` — move to `Domain/Tokens/` (phase 7).
- `auth-microservice/Auth.Tests/Qa/AuthQaSeedTests.cs` — namespace update only.

New files (created):

- `auth-microservice/Auth.Service.LayoutAnalyzer/LayoutAnalyzer.cs` — port from Basket (phase 1).
- `auth-microservice/Auth.Service/Domain/Abstractions/IAuthStore.cs` (phase 3).
- `auth-microservice/Auth.Service/Domain/Abstractions/IRsaKeyProvider.cs` (phase 5).
- `auth-microservice/Auth.Service/Infrastructure/Data/EntityFramework/EfAuthStore.cs` (phase 3).
- `auth-microservice/Auth.Service/Infrastructure/Signing/SigningInfrastructureExtensions.cs` (phase 5).
- `auth-microservice/Auth.Service/Features/Login/{LoginEndpoint,LoginHandler,LoginRequest,LoginSliceExtensions}.cs` (phase 6).
- `auth-microservice/Auth.Service/Features/IssueServiceToken/{IssueServiceTokenEndpoint,IssueServiceTokenHandler,ServiceClientOptions,ServiceClient,IssueServiceTokenSliceExtensions}.cs` (phase 6).
- `auth-microservice/Auth.Service/Features/GetJwks/{GetJwksEndpoint,GetJwksHandler,Jwk,JwksDocument,GetJwksSliceExtensions}.cs` (phase 6).
- `auth-microservice/Auth.Service/Features/GetOpenIdConfiguration/{GetOpenIdConfigurationEndpoint,GetOpenIdConfigurationHandler,OpenIdConfigurationDocument,GetOpenIdConfigurationSliceExtensions}.cs` (phase 6).
- `auth-microservice/Auth.Tests/Architecture/LayoutTests.cs` (phase 1, unskipped phase 8).
- `auth-microservice/Auth.Tests/Infrastructure/Data/EntityFramework/EfAuthStoreTests.cs` (phase 3).
- `auth-microservice/Auth.Tests/Features/Login/{LoginEndpointTests,LoginHandlerTests}.cs` (phase 7).
- `auth-microservice/Auth.Tests/Features/IssueServiceToken/{IssueServiceTokenEndpointTests,IssueServiceTokenHandlerTests}.cs` (phase 7).
- `auth-microservice/Auth.Tests/Features/GetJwks/GetJwksEndpointTests.cs` (phase 7).
- `auth-microservice/Auth.Tests/Features/GetOpenIdConfiguration/GetOpenIdConfigurationEndpointTests.cs` (phase 7).
- `auth-microservice/Auth.Tests/Domain/Tokens/{JwtTokenServiceTests,ServiceTokenServiceTests}.cs` (phase 7).

Reference templates (read-only):

- `basket-microservice/Basket.Service.LayoutAnalyzer/LayoutAnalyzer.cs` — analyzer template (phase 1).
- `basket-microservice/Basket.Tests/Architecture/LayoutTests.cs` — NetArchTest template (phase 1).
- `basket-microservice/Basket.Service/Features/AddBasketProduct/` — slice shape template (phase 6).
- `basket-microservice/Basket.Service/Program.cs` — manifest-style composition root (phase 6).

## Out of scope (per PRD)

- Refactoring any other service (inventory, shipping, payment, saga, api-gateway, basket, product, order). Propagation handled by a follow-up ADR.
- Modifying `ECommerce.Shared`. Pilot composes existing `AddJwtAuthentication`, `AddPlatformObservability`, `AddPlatformHealthChecks`, `AddPlatformOpenApi`, `QaSeedingExtensions`.
- Adding request validation (FluentValidation or DataAnnotations).
- Introducing MediatR or any mediator-style dispatcher.
- Splitting `Auth.Service.csproj` into multiple application-tier projects.
- Changing the `User` schema, the `dev-keys/` PEMs, or any EF migration.
- Changing JWT claims, signing algorithm (RS256), token lifetimes (15 minutes), or issuer derivation.
- Changing the `/login`, `/token`, `/.well-known/jwks.json`, `/.well-known/openid-configuration` public surfaces.
- Changing the metric counter names.
- Changing the dummy-hash timing-defense behavior.
- Changing CI/CD pipelines, Docker Compose, Kubernetes manifests, or Azure pipelines.
- Promoting `User` to a behavioral aggregate.
- Introducing domain events, an outbox, or integration events.
- Re-abstracting `IPasswordHasher<User>`.

## Post-merge follow-ups (separate ADR)

After Auth lands, file a propagation ADR covering inventory → payment → shipping → saga in that order. The ADR should re-validate the pattern against four diverse pilots (Order: rich aggregate + outbox + saga participant; Product: catalog + outbox + integration events; Basket: Redis-only + no outbox + no events; Auth: SQL + no outbox + no events + RSA signing) before propagating further.
