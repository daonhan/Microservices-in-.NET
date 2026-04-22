# Basket Service

Shopping-cart service. Stores baskets in Redis and caches product prices it has seen via the events bus. Does not own a SQL database.

| | |
|---|---|
| **Port** | 8000 |
| **Datastore** | Redis |
| **Source** | [`basket-microservice/Basket.Service/`](https://github.com/daonhan/Microservices-in-.NET/tree/main/basket-microservice/Basket.Service) |
| **Tests** | [`basket-microservice/Basket.Tests/`](https://github.com/daonhan/Microservices-in-.NET/tree/main/basket-microservice/Basket.Tests) |
| **Publishes** | none |
| **Subscribes** | `OrderCreatedEvent`, `ProductPriceUpdatedEvent` |

## Responsibilities

- CRUD for customer baskets keyed by `customerId`.
- Maintain a local price cache so the basket can show prices without calling the Product service synchronously.
- Clear the basket when an order is placed.

## HTTP endpoints

All endpoints require a valid JWT at the Gateway.

| Method | Route | Purpose |
|---|---|---|
| `GET` | `/basket/{customerId}` | Retrieve a customer's basket |
| `POST` | `/basket/{customerId}` | Create an empty basket |
| `PUT` | `/basket/{customerId}` | Add a product to the basket |
| `DELETE` | `/basket/{customerId}/{productId}` | Remove a single product |
| `DELETE` | `/basket/{customerId}` | Delete the basket |

Implementation: `Endpoints/BasketApiEndpoints.cs`.

See [API-Reference](API-Reference) for consolidated listing.

## Events

- **Subscribes `ProductPriceUpdatedEvent`** — updates the cached price for the product in Redis, so subsequent basket reads show the new price.
- **Subscribes `OrderCreatedEvent`** — clears the customer's basket once their order is persisted upstream.

Full payloads in [Integration-Events](Integration-Events).

## Structure

```
Basket.Service/
├── Program.cs
├── Endpoints/BasketApiEndpoints.cs
├── ApiModels/             # request/response DTOs
├── Models/                # domain entities
├── Infrastructure/        # Redis access, event handlers
└── IntegrationEvents/     # subscribed events + handlers
```

## Configuration

- Redis connection string: `ConnectionStrings:Redis`
- RabbitMQ: `RabbitMq:*`
- JWT: `Jwt:*` (shared with [Service-Auth](Service-Auth))

See `appsettings.json` and `appsettings.Development.json` in the service folder.
