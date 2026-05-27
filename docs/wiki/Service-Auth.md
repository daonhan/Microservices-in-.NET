# Auth Service

Issues JWT access tokens. The only service with no event-bus dependency.

| | |
|---|---|
| **Port** | 8003 |
| **Datastore** | SQL Server (database: `Auth`) |
| **Source** | [`auth-microservice/Auth.Service/`](https://github.com/daonhan/Microservices-in-.NET/tree/main/auth-microservice/Auth.Service) |
| **Tests** | [`auth-microservice/Auth.Tests/`](https://github.com/daonhan/Microservices-in-.NET/tree/main/auth-microservice/Auth.Tests) |
| **Publishes** | none |
| **Subscribes** | none |
| **Layout** | Clean Architecture + Vertical Slices default ([ADR-0012](https://github.com/daonhan/Microservices-in-.NET/blob/main/docs/adr/0012-clean-arch-vsa-default-service-shape.md)); Auth omits `Contracts/Integration/` because it has no broker contracts. |

## HTTP endpoints

| Method | Route | Auth | Purpose |
|---|---|---|---|
| `POST` | `/login` | public | Validate user credentials and return a user JWT |
| `POST` | `/token` | public | OAuth2 `client_credentials` grant — mints a service-role JWT for trusted backends |

Implementations: `Features/Login/LoginEndpoint.cs`, `Features/IssueServiceToken/IssueServiceTokenEndpoint.cs`, `Features/GetJwks/GetJwksEndpoint.cs`, `Features/GetOpenIdConfiguration/GetOpenIdConfigurationEndpoint.cs`.

## Token formats

| Token kind | Endpoint | Algorithm | Key claim | Consumers |
|---|---|---|---|---|
| User token | `/login` | **RSA-SHA256** | `user_role` (e.g. `Administrator`) | Gateway + services enforcing role policies (see [Service-Inventory](Service-Inventory), [Service-Product](Service-Product)) |
| Service token | `/token` | **RSA-SHA256** | `user_role=service` | Downstream `/internal/*` endpoints guarded by `RequireService` (see [Shared-Library](Shared-Library#authorization-policies)) |

- Issuer validation: `AuthMicroserviceBaseAddress` config key across services must match.
- Service clients are seeded from `ServiceClients` configuration (`appsettings.{Environment}.json`); requests use `application/x-www-form-urlencoded` form fields `grant_type=client_credentials`, `client_id`, `client_secret`. Only `client_credentials` is accepted; any other grant returns `400 unsupported_grant_type`.
- Service-token lifetime is 15 minutes. Public RSA keys are exposed via the JWKS endpoint so verifiers can rotate without redeploys.

## Shared validation

Every downstream service wires JWT validation through the shared library:

```csharp
builder.Services.AddJwtAuthentication(builder.Configuration);
app.UseJwtAuthentication();
```

See [Shared-Library](Shared-Library) for what these do internally.

## Migrations

- `20260414105802_InitialCreate`

## Structure

```
Auth.Service/
├── Program.cs
├── Features/
│   ├── Login/                     # /login (user tokens, RS256)
│   ├── IssueServiceToken/         # /token (client_credentials, RS256)
│   ├── GetJwks/
│   └── GetOpenIdConfiguration/
├── Domain/                        # user model, token services, RSA key provider abstractions
├── Infrastructure/                # EF Core data + signing infrastructure
└── Migrations/
```
