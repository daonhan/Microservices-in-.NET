# Testing

Every service has its own test project. The house rules below keep the tests fast, deterministic, and focused on external behavior.

## Stack

- **xUnit** — test runner
- **NSubstitute** — mocking
- **`WebApplicationFactory<Program>`** — in-process integration host
- **`IAsyncLifetime`** — per-class setup/teardown for real dependencies

All three are referenced consistently — for example [`basket-microservice/Basket.Tests/Basket.Tests.csproj`](https://github.com/daonhan/Microservices-in-.NET/blob/main/basket-microservice/Basket.Tests/Basket.Tests.csproj).

## Conventions

| Convention | Rule |
|---|---|
| Test naming | `Given_When_Then` (e.g. `Given_NoBasket_When_GetBasket_Then_ReturnsNotFound`) |
| Unit tests | Arrange-Act-Assert blocks, one behavior per test |
| Mocks | Only at architectural seams (event bus, clock, repository when necessary) |
| Integration tests | Real SQL Server / Redis / RabbitMQ via containers or the `docker compose up sql rabbitmq redis` dev stack |
| Cleanup | `IAsyncLifetime.DisposeAsync` wipes per-test data |

## Unit tests

Live under `{service}-microservice/{Service}.Tests/Domain/` and `.../Endpoints/`. Example: [`basket-microservice/Basket.Tests/`](https://github.com/daonhan/Microservices-in-.NET/tree/main/basket-microservice/Basket.Tests).

Rule of thumb: if a test imports `NSubstitute` and does not start the host, it's a unit test.

## Integration tests

Live under `{service}-microservice/{Service}.Tests/Api/` or `.../Integration/`. Example: [`order-microservice/Order.Tests/`](https://github.com/daonhan/Microservices-in-.NET/tree/main/order-microservice/Order.Tests) contains:

- `OrderWebApplicationFactory.cs` — custom `WebApplicationFactory<Program>` that rewires the DB connection string
- `IntegrationTestBase.cs` — RabbitMQ subscription fixture for saga assertions
- `OrderApiTests.cs`, `StockReservationFailedTests.cs`, `HealthChecksTests.cs`

The gateway has its own integration suite under [`api-gateway/ApiGateway.Tests/`](https://github.com/daonhan/Microservices-in-.NET/tree/main/api-gateway/ApiGateway.Tests).

## Event round-trip tests

For services that publish or subscribe, tests assert end-to-end on a real broker:

1. Arrange: spin up the service via `WebApplicationFactory`, subscribe a test handler to the target event.
2. Act: call the endpoint that should trigger a publish.
3. Assert: the test handler receives the event within a bounded timeout.

## What we don't test

- Framework plumbing (DI wiring, option binding). ASP.NET Core tests its own plumbing.
- Private implementation details. Tests that assert method call counts on internal types rot quickly.
- Wiki content (see [PRD-Wiki](https://github.com/daonhan/Microservices-in-.NET/blob/main/docs/prd/PRD-Wiki.md)).

## Running

```bash
cd {service}-microservice && dotnet test
```

Integration tests need `sql`, `rabbitmq`, and `redis` up. In local dev:

```bash
docker compose up sql rabbitmq redis -d
```
