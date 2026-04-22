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

## HTTP endpoints

| Method | Route | Auth | Purpose |
|---|---|---|---|
| `POST` | `/login` | public | Validate credentials and return a JWT |

Implementation: `Endpoints/AuthApiEndpoints.cs`.

## Token format

- Algorithm: **HMAC-SHA256**
- Claims include `user_role` (values such as `Administrator`) — consumed by the Gateway and by services that enforce role-based policies (see [Service-Inventory](Service-Inventory) and [Service-Product](Service-Product)).
- Issuer validation: `AuthMicroserviceBaseAddress` config key across services must match.

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
├── Endpoints/AuthApiEndpoints.cs
├── ApiModels/
├── Models/
├── Services/               # token issuance, password hashing
├── Infrastructure/Data/
└── Migrations/
```
