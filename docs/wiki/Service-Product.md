# Product Service

Product catalog. Owns the authoritative product record and emits events when products are created or their price changes.

| | |
|---|---|
| **Port** | 8002 |
| **Datastore** | SQL Server (database: `Product`) |
| **Source** | [`product-microservice/Product.Service/`](https://github.com/daonhan/Microservices-in-.NET/tree/main/product-microservice/Product.Service) |
| **Tests** | [`product-microservice/Product.Tests/`](https://github.com/daonhan/Microservices-in-.NET/tree/main/product-microservice/Product.Tests) |
| **Publishes** | `ProductCreatedEvent`, `ProductPriceUpdatedEvent` |
| **Subscribes** | none |

## HTTP endpoints

| Method | Route | Auth | Purpose |
|---|---|---|---|
| `GET` | `/product/{productId}` | public | Read a product |
| `POST` | `/product` | Bearer + `Administrator` | Create a product |
| `PUT` | `/product/{productId}` | Bearer + `Administrator` | Update a product |

Write endpoints are restricted at the Gateway via a role policy. See [Service-API-Gateway](Service-API-Gateway).

Implementation: `Endpoints/ProductApiEndpoints.cs`.

## Events

- `ProductCreatedEvent` — published via Outbox after insert. Consumers use it to warm caches and inventory records.
- `ProductPriceUpdatedEvent` — published via Outbox when price changes. [Service-Basket](Service-Basket) subscribes to refresh its price cache.

Payloads: [Integration-Events](Integration-Events).

## Migrations

- `20260414081144_Initial`
- `20260414081154_SeedProductTypes`

## Structure

```
Product.Service/
├── Program.cs
├── Endpoints/ProductApiEndpoints.cs
├── ApiModels/
├── Models/
├── Infrastructure/Data/
├── IntegrationEvents/
└── Migrations/
```
