# Auth Service Clean Architecture + Vertical Slices Pilot PRD

## Problem Statement

`Auth.Service` is organized by technical type: HTTP endpoints in `Endpoints/`, DTOs in `ApiModels/`, anemic data classes in `Models/`, token issuers in `Services/`, signing helpers in `Services/Signing/`, persistence in `Infrastructure/Data/`. Understanding or changing a single feature ("what happens at `POST /login`?") requires hopping across four or five folders, and the persistence layer leaks policy — `AuthContext.VerifyUserLogin` mixes EF data access with `IPasswordHasher` verification and a timing-defense dummy-hash side channel, the same shape of smell that `OrderContext.Translate` had before the Order pilot. Domain/application/infrastructure boundaries exist only as conventions and erode silently, especially under AI-assisted edits. The Order, Product, and Basket pilots already established the Clean Architecture + Vertical Slice layout; auth was always on the propagation list (ADR-0011), and it is the next candidate after Basket.

## Solution

Refactor `Auth.Service` to the Clean Architecture + VSA layout established by ADR-0011, with **zero behavior change**. Inside the existing `Auth.Service.csproj`, reorganize into:

- `Features/<Slice>/` — one folder per inbound HTTP trigger. Slices: `Login`, `IssueServiceToken`, `GetJwks`, `GetOpenIdConfiguration`. Each is self-contained: endpoint, request/response DTOs, sealed handler, slice DI extension.
- `Domain/` — `User`, `AuthToken` records, `Domain/Abstractions/IAuthStore`, `Domain/Abstractions/IRsaKeyProvider`, and the token-builder domain services `Domain/Tokens/JwtTokenService` and `Domain/Tokens/ServiceTokenService`. No EF, no HTTP, no file-system references.
- `Infrastructure/Data/EntityFramework/` — `AuthContext` (persistence only) and `EfAuthStore` implementing `IAuthStore.FindByUsernameAsync`.
- `Infrastructure/Signing/` — `PemFileRsaKeyProvider`, `SigningOptions`, infrastructure DI extension.

Slice handlers are invoked via plain DI (no MediatR). Password verification moves from `AuthContext` into `LoginHandler`, which preserves the timing-defense dummy-hash path. `JwtTokenService` and `ServiceTokenService` keep their existing public surfaces; only their location and namespace change. Boundaries are enforced both by NetArchTest rules in `Auth.Tests/Architecture/LayoutTests.cs` and by a Roslyn `Auth.Service.LayoutAnalyzer` (mirrors Basket's analyzer). Tests are reshaped to mirror slices. Pilot composes ADR-0011 by reference (no new ADR), reuses the existing "adding a new slice" runbook unchanged, and leaves `ECommerce.Shared` untouched. The work lands as staged commits on one branch and merges via one PR.

## User Stories

1. As an Auth service developer, I want to open a single folder to see everything the "login" feature does, so that I do not have to reconstruct the feature from four scattered folders.
2. As an Auth service developer, I want each slice to register its own dependencies via an `AddXxxSlice()` extension, so that adding a new feature is a drop-in change and `Program.cs` reads like a manifest.
3. As an Auth service developer, I want to add a new HTTP endpoint by creating one new `Features/<Name>/` folder, so that I never need to touch unrelated handlers or DTOs.
4. As an Auth service developer, I want `Domain/Tokens/JwtTokenService` and `Domain/Tokens/ServiceTokenService` to live alongside `Domain/User` and `Domain/AuthToken`, so that token-issuance policy is grouped with the types it operates on rather than buried in `Services/`.
5. As an Auth service developer, I want `IAuthStore.FindByUsernameAsync` to be the only persistence concern of `AuthContext`, so that password verification is a slice responsibility and `AuthContext` becomes a single-purpose persistence module.
6. As an Auth service developer, I want `LoginHandler` to own password verification — including the dummy-hash timing-defense path — so that `IPasswordHasher` policy is co-located with the slice that depends on it.
7. As an Auth service developer, I want `GetJwks` and `GetOpenIdConfiguration` as two separate slices, so that one inbound trigger maps to one folder per the runbook rule.
8. As an Auth service developer, I want `IssueServiceToken` (client_credentials) to own `ServiceClientOptions`, so that the client-secret registry is co-located with the only slice that uses it.
9. As an Auth service developer, I want `IRsaKeyProvider` to be a Domain abstraction implemented by `Infrastructure/Signing/PemFileRsaKeyProvider`, so that token-builder domain services do not depend on file-system details.
10. As an Auth service maintainer, I want NetArchTest rules that fail the test suite if `Domain` references infrastructure, if any slice references another slice, or if infrastructure leaks past `Domain`, so that boundary violations are caught in CI rather than in code review.
11. As an Auth service maintainer, I want a Roslyn `Auth.Service.LayoutAnalyzer` as a second guardrail beside NetArchTest, so that violations surface as compiler errors during development, not only when tests run.
12. As an Auth service maintainer, I want `AuthContext` reduced to a `DbContext` with no policy, so that future changes to password hashing or login flow do not touch persistence code.
13. As an Auth service contributor, I want the cross-slice sharing rule documented as "duplicate first, extract on third" with a NetArchTest rule forbidding slice-to-slice references, so that I do not accidentally create a hidden coupling between two slices.
14. As an Auth service contributor, I want namespaces to match the new folder layout (`Auth.Service.Domain`, `Auth.Service.Domain.Tokens`, `Auth.Service.Features.Login`, `Auth.Service.Infrastructure.Data.EntityFramework`, `Auth.Service.Infrastructure.Signing`), so that I can grep for layer membership and analyzer rules can target namespaces.
15. As an Auth service contributor, I want `Auth.Tests` to mirror `Features/<Slice>/` and keep `Domain/` token-service unit tests separate, so that feature tests and domain unit tests are each easy to locate.
16. As a reviewer, I want the pilot to land as staged commits on one branch and a single PR, with each commit building and tests passing, so that the refactor is bisectable and reviewable end-to-end.
17. As a reviewer, I want **zero behavior change** from the pilot — every existing Auth test passes after only namespace updates, and the dummy-hash timing-defense behavior of `POST /login` is preserved — so that the layout migration cannot regress functional behavior.
18. As a reviewer, I want the JWKS `/jwks` and `/openid-configuration` endpoints to keep the same JSON shape, `Cache-Control: public, max-age=300` header, and `jwks-served` metric, so that JWT-validating consumers continue to work unchanged.
19. As a reviewer, I want `POST /login` and `POST /token` to keep identical response shapes, status codes, and metric counters (`login-success`, `login-failure`, `service-token-success`, `service-token-failure`), so that observability dashboards do not break.
20. As a release engineer, I want the pilot to leave `ECommerce.Shared` untouched (no nupkg version bump), so that other services are not forced to consume a new shared package version.
21. As a release engineer, I want the pre-commit hook (`dotnet format`, `dotnet build`, Basket tests) to gate every commit on the refactor branch, so that the branch cannot accumulate partial-validation commits.
22. As an architect, I want the pilot to compose ADR-0011 by reference (matching Basket and Product) and reuse the existing "adding a new slice" runbook unchanged, so that no new ADR is created and the rationale stays single-sourced.
23. As an architect, I want the decision to propagate the pattern to the remaining services (inventory, shipping, payment, saga) to be a separate ADR after auth lands, so that propagation is informed by what we learned from all four pilots.
24. As an AI-assisted contributor, I want the layout, namespaces, and architecture rules to be self-describing and analyzer-enforced, so that AI edits cannot silently drift across boundaries.
25. As an operator, I want the database schema, EF migrations, and seeded QA data to be unchanged after the refactor, so that no migration step is needed at deploy time.

## Implementation Decisions

### Pilot scope

- Pilot is `Auth.Service` only. No other service changes. Propagation handled by a follow-up ADR.

### Project shape

- Single `Auth.Service.csproj` retained. No split into `Auth.Domain` / `Auth.Application` / `Auth.Infrastructure` projects.
- Boundaries enforced by namespace conventions + analyzer rules + architecture tests, not by csproj references.

### Folder topology

- `Features/Login/` — `LoginEndpoint`, `LoginRequest`, sealed `LoginHandler` (orchestrates `IAuthStore.FindByUsernameAsync` + `IPasswordHasher<User>` + `JwtTokenService`), `LoginSliceExtensions.AddLoginSlice`.
- `Features/IssueServiceToken/` — `IssueServiceTokenEndpoint`, sealed `IssueServiceTokenHandler` (orchestrates `ServiceClientOptions` lookup + `ServiceTokenService`), `ServiceClientOptions` (slice-local because only this slice uses it), `IssueServiceTokenSliceExtensions.AddIssueServiceTokenSlice`.
- `Features/GetJwks/` — `GetJwksEndpoint`, `Jwk` / `JwksDocument` response records, `GetJwksSliceExtensions.AddGetJwksSlice`.
- `Features/GetOpenIdConfiguration/` — `GetOpenIdConfigurationEndpoint`, `OpenIdConfigurationDocument` response record, `GetOpenIdConfigurationSliceExtensions.AddGetOpenIdConfigurationSlice`.
- `Domain/` — `User`, `AuthToken` records; `Domain/Abstractions/IAuthStore`, `Domain/Abstractions/IRsaKeyProvider` (`PublishedKey` record co-located); `Domain/Tokens/JwtTokenService`, `Domain/Tokens/ServiceTokenService`.
- `Infrastructure/Data/EntityFramework/` — `AuthContext` (persistence only), `EfAuthStore` (impl of `IAuthStore`), `AuthContextDatabaseMigration`, `Configurations/UserConfiguration`, `AuthDataExtensions.AddAuthDatastore` (renamed from `AddSqlServerDatastore` because the current name collides with the Shared helper).
- `Infrastructure/Signing/` — `PemFileRsaKeyProvider`, `SigningOptions`, `SigningInfrastructureExtensions.AddSigningInfrastructure`.
- `Migrations/` — unchanged; `generated_code = true`.
- No `Contracts/` folder. Auth produces and consumes no cross-service payloads.

### Dispatch model

- No MediatR. No mediator. No reflection-based pipeline.
- Endpoints take their slice handler class via constructor or parameter-binding injection and call `HandleAsync(...)` directly.
- Slice handlers are `internal sealed` classes with one public async (or sync, for the synchronous `IssueServiceTokenHandler`) method.

### Token-builder placement

- `JwtTokenService` and `ServiceTokenService` move to `Domain/Tokens/`. Public method signatures preserved where the consumer surface allows.
- `JwtTokenService.GenerateAuthenticationToken` signature becomes `GenerateAuthenticationToken(User user)` — the password parameter is removed because verification is no longer the token-builder's concern (it is now `LoginHandler`'s). Existing unit tests are adjusted only where they passed `(string, string)`; the assertion surface (claims, expiry, signing) is unchanged.
- `ServiceTokenService.GenerateServiceToken(clientId, clientSecret)` keeps its signature; the slice handler delegates directly.

### `IAuthStore` split

- `IAuthStore.VerifyUserLogin` is **removed**. Replaced by `IAuthStore.FindByUsernameAsync(string username)` returning `User?`.
- `EfAuthStore` (new class in `Infrastructure/Data/EntityFramework/`) implements `FindByUsernameAsync`. `AuthContext` no longer implements `IAuthStore`.
- `LoginHandler` calls `IAuthStore.FindByUsernameAsync(username)`, then runs `IPasswordHasher<User>.VerifyHashedPassword`. The dummy-hash timing-defense path moves into `LoginHandler` verbatim — same dummy hash constant, same call shape, same return-`null` behavior.

### Composition root as manifest

`Program.cs` becomes a fluent manifest:

```
builder.Services.AddAuthDatastore(builder.Configuration)
                .AddSigningInfrastructure(builder.Configuration)
                .AddLoginSlice(builder.Configuration)
                .AddIssueServiceTokenSlice(builder.Configuration)
                .AddGetJwksSlice()
                .AddGetOpenIdConfigurationSlice();
// AddPlatformObservability / AddPlatformHealthChecks / AddPlatformOpenApi unchanged
var app = builder.Build();
// UsePrometheusExporter / MapPlatformHealthChecks / UsePlatformOpenApi / Qa seeding unchanged
app.MapLogin();
app.MapIssueServiceToken();
app.MapGetJwks();
app.MapGetOpenIdConfiguration();
```

`RegisterTokenService` is deleted (decomposed into `AddSigningInfrastructure` + slice extensions).

### Cross-slice sharing rule

- Rule of three: duplicate freely between slices; extract to `Domain/` (behavioral) or `Features/Shared/` (helper) only on the third use.
- NetArchTest forbids `Auth.Service.Features.<X>` referencing `Auth.Service.Features.<Y>` for any `X != Y`.

### Boundary enforcement

- NetArchTest rules in `Auth.Tests/Architecture/LayoutTests.cs`, mirroring Basket:
  1. `Auth.Service.Domain.*` may not reference `Auth.Service.Infrastructure.*` or `Auth.Service.Features.*`.
  2. `Auth.Service.Features.<X>.*` may not reference `Auth.Service.Features.<Y>.*` for any `X != Y`.
  3. `Auth.Service.Infrastructure.*` may not reference `Auth.Service.Features.*`.
  4. Not applicable: no `Contracts/` folder. Rule (4) from Basket is omitted; absence documented in the PR description.
- Roslyn `Auth.Service.LayoutAnalyzer` raises the same three rules as build-time compiler errors via `.editorconfig`.

### Namespaces

- `Auth.Service.Domain`, `Auth.Service.Domain.Abstractions`, `Auth.Service.Domain.Tokens`
- `Auth.Service.Features.Login`, `Auth.Service.Features.IssueServiceToken`, `Auth.Service.Features.GetJwks`, `Auth.Service.Features.GetOpenIdConfiguration`
- `Auth.Service.Infrastructure.Data.EntityFramework`, `Auth.Service.Infrastructure.Signing`

### Shared library

- `ECommerce.Shared` is not modified. No nupkg version bump. Auth has no RabbitMQ / outbox, so the lazy-singleton fix from the Order pilot does not apply.

### `AddSqlServerDatastore` rename

- Auth's current extension is named `AddSqlServerDatastore`, identical to the Shared helper. Pilot renames it `AddAuthDatastore` to avoid the name collision; no behavior change.

### Validation

- Out of scope. Current absence of `FluentValidation` / `DataAnnotations` is preserved. Per-slice request validation is a follow-up.

### Rollout

- Branch `refactor/auth-cleanarch-vsa`. Staged commits land in this order, each green:
  1. Scaffold NetArchTest dependency in `Auth.Tests` + skipped layout tests; add `Auth.Service.LayoutAnalyzer` csproj scaffolded but not yet wired.
  2. Move `User`, `AuthToken` to `Domain/`; rename namespaces.
  3. Introduce `Domain/Abstractions/IAuthStore.FindByUsernameAsync` + `EfAuthStore`; remove `IAuthStore` implementation from `AuthContext`; update `LoginHandler` to own password verification + dummy-hash timing defense.
  4. Move `JwtTokenService`, `ServiceTokenService` to `Domain/Tokens/`; adjust `JwtTokenService` signature to take `User`.
  5. Move `IRsaKeyProvider` to `Domain/Abstractions/`; move `PemFileRsaKeyProvider` + `SigningOptions` to `Infrastructure/Signing/`; introduce `AddSigningInfrastructure`.
  6. Extract slices one at a time: `Login`, `IssueServiceToken`, `GetJwks`, `GetOpenIdConfiguration`. Each commit independently green.
  7. Reshape `Auth.Tests` to mirror slices.
  8. Wire the Roslyn `LayoutAnalyzer`; unskip NetArchTest rules.
  9. Update root `CLAUDE.md` to note Auth as the fourth Clean Architecture + VSA pilot (composes ADR-0011 by reference).
- Single PR for review. Pre-commit hook gates every commit (no `--no-verify`, no validation deferral, per root `CLAUDE.md`).

## Testing Decisions

### Test philosophy

- A good test verifies external behavior of a module through its public interface, not internal implementation details.
- Refactor must produce zero behavior change. Every existing `Auth.Tests` test must continue to pass with only namespace updates and the small set of signature-driven adjustments documented below.
- New tests are added only for new seams (`EfAuthStore`, the `LoginHandler` timing-defense path now that it owns verification) and for the architecture rules themselves.

### Modules to test

- **`JwtTokenService` (`Domain/Tokens/`)** — existing `JwtTokenServiceTests` covers token claims, expiry, signing algorithm. Adjusted only to call the new `(User)` signature. Assertion surface unchanged.
- **`ServiceTokenService` (`Domain/Tokens/`)** — existing `ServiceTokenServiceTests` kept verbatim modulo namespace.
- **`LoginHandler` (new, `Features/Login/`)** — new unit tests covering: unknown username → `null` and dummy-hash path executed; valid username + wrong password → `null`; valid credentials → `AuthToken`. Uses `NSubstitute` on `IAuthStore` and a real `PasswordHasher<User>`.
- **`IssueServiceTokenHandler` (new, `Features/IssueServiceToken/`)** — unit tests covering: unknown `client_id`, wrong `client_secret`, valid → `AuthToken`. Mirrors current `ServiceTokenEndpointTests` shape at the handler level.
- **Per-slice endpoint tests** — `Auth.Tests/Features/<Slice>/<Slice>EndpointTests.cs` use `WebApplicationFactory<Program>` (Auth already exposes `public partial class Program { }`). Cover existing endpoint-level assertions: status codes, response shapes, metric counters, `Cache-Control` header on discovery endpoints.
- **`EfAuthStore` (new, `Infrastructure/Data/EntityFramework/`)** — focused tests for `FindByUsernameAsync` against a real `AuthContext`: returns `null` for unknown username, returns the entity for a seeded user. Replaces the current `AuthContextVerifyUserLoginTests` whose subject (verification) has moved into `LoginHandler`.
- **`Auth.Tests/Architecture/LayoutTests.cs`** — three NetArchTest rules (Domain, Features.<X>↛<Y>, Infrastructure↛Features). Executable specification of the boundary policy.
- **QA seeding** (`Auth.Tests/Qa/AuthQaSeedTests.cs`) — unchanged modulo namespace.

### Prior art in the codebase

- `Basket.Tests/Architecture/LayoutTests.cs` is the closest template (no `Contracts/`, no outbox); copy and rename.
- `basket-microservice/Basket.Service.LayoutAnalyzer/LayoutAnalyzer.cs` is the closest analyzer template; copy and rename.
- `Order.Tests/IntegrationTestBase.cs` / `Basket.Tests/Features/*` show the per-slice endpoint-test pattern with `WebApplicationFactory<Program>`.
- Pre-commit hook (`dotnet husky run --group pre-commit`) enforces `dotnet format --verify-no-changes` and `dotnet build --no-restore` + Basket tests on every commit. Auth tests run manually per the root `CLAUDE.md` sandbox policy before pushing.

## Out of Scope

- Refactoring any other service (inventory, shipping, payment, saga, api-gateway, basket, product, order). Propagation handled by a follow-up ADR.
- Modifying `ECommerce.Shared`. The pilot composes existing `AddJwtAuthentication`, `AddPlatformObservability`, `AddPlatformHealthChecks`, `AddPlatformOpenApi`, `QaSeedingExtensions`.
- Adding request validation (FluentValidation or DataAnnotations).
- Introducing MediatR or any mediator-style dispatcher.
- Splitting `Auth.Service.csproj` into multiple projects.
- Changing the `User` schema, the `dev-keys/` PEMs, or any EF migration.
- Changing JWT claims, signing algorithm (`RS256`), token lifetimes (15 minutes), or issuer derivation.
- Changing the `/login`, `/token`, `/.well-known/jwks.json`, or `/.well-known/openid-configuration` public surfaces (routes, status codes, response shapes, headers).
- Changing the metric counter names (`login-success`, `login-failure`, `service-token-success`, `service-token-failure`, `jwks-served`).
- Changing the dummy-hash timing-defense behavior.
- Changing CI/CD pipelines, Docker Compose, Kubernetes manifests, or Azure pipelines.
- Promoting `User` to a behavioral aggregate. Stays an anemic record.
- Introducing domain events, an outbox, or integration events. Auth produces and consumes none.

## Further Notes

- Auth is the fourth Clean Architecture + VSA pilot, after Order, Product, Basket. Like Basket, it has no outbox seam and (unlike Order/Product/Basket) no integration events — so its `Contracts/` folder is omitted entirely and the fourth boundary rule from the Order/Basket layout tests does not apply. This divergence is documented in the PR description; ADR-0011 is composed by reference and not re-litigated.
- The most concrete pre-existing smell the pilot must resolve is `AuthContext.VerifyUserLogin`, which mixes EF persistence with `IPasswordHasher` verification and a dummy-hash timing-defense side channel. Splitting `IAuthStore` to `FindByUsernameAsync` and moving verification + timing defense into `LoginHandler` is the deepest module change in the pilot; everything else is relocation + namespace renames.
- `IRsaKeyProvider` is a Domain abstraction (consumed by `JwtTokenService` and `ServiceTokenService` in `Domain/Tokens/`) implemented by `PemFileRsaKeyProvider` in `Infrastructure/Signing/`. `IPasswordHasher<User>` stays as the `Microsoft.AspNetCore.Identity` type — not re-abstracted, because the only consumer (`LoginHandler`) already depends on `Microsoft.AspNetCore.Identity` and re-abstraction would add a wrapper without a second consumer.
- `ServiceClientOptions` is slice-local under `Features/IssueServiceToken/`. The "rule of three" applies: if a second slice needs it, extract to `Infrastructure/`.
- Candidate propagation order after auth lands: **inventory → payment → shipping → saga**. That is the remainder of the original ADR-0011 propagation list once auth is done.

---

Epic: #152 (Order pilot). Composes ADR-0011 by reference.
