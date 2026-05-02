# Getting Started

This page gets a developer from a fresh clone to a running stack with traces, metrics, and logs flowing. If anything here drifts from the repo, the [README](https://github.com/daonhan/Microservices-in-.NET#getting-started) is the source of truth.

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- [Docker Desktop](https://www.docker.com/products/docker-desktop/)
- [kubectl](https://kubernetes.io/docs/tasks/tools/) (optional — only for Kubernetes)
- A shell (PowerShell, bash, or zsh)

## Option A — Full stack with Docker Compose

```bash
docker compose up --build
```

This starts eight microservices plus SQL Server, RabbitMQ, Redis, OTel Collector, Jaeger, Prometheus, Alertmanager, Grafana, Loki, and Prometheus exporters for RabbitMQ, Redis, and SQL Server.

Verify endpoints:

| Endpoint | URL |
|---|---|
| API Gateway | http://localhost:8004 |
| Combined Swagger UI (dev/staging only) | http://localhost:8004/swagger |
| RabbitMQ Management | http://localhost:15672 (guest/guest) |
| Jaeger UI | http://localhost:16686 |
| Prometheus | http://localhost:9090 |
| Alertmanager | http://localhost:9093 |
| Grafana | http://localhost:3000 |
| Loki | http://localhost:3100 |

The Swagger UI is the easiest way to explore endpoints across all services: pick a service from the dropdown, click **Authorize**, paste a JWT returned by `POST /login`, and Try-it-out works for every service through the gateway. See [Service-API-Gateway § Combined Swagger UI](Service-API-Gateway#combined-swagger-ui).

## Option B — Run one service against shared infra

Start just the infrastructure, then run a service from your IDE or the CLI.

```bash
docker compose up sql rabbitmq redis -d

cd product-microservice/Product.Service
dotnet run
```

Repeat for whichever service you're working on. The gateway can be run the same way (`cd api-gateway/ApiGateway && dotnet run`).

## First request — end-to-end smoke test

1. Login to get a JWT (default dev user is seeded by Auth migrations):
   ```bash
   curl -X POST http://localhost:8004/login \
     -H "Content-Type: application/json" \
     -d '{"username":"admin","password":"admin"}'
   ```
2. Call a public endpoint:
   ```bash
   curl http://localhost:8004/product/1
   ```
3. Call an authenticated endpoint using the token from step 1:
   ```bash
   curl http://localhost:8004/basket/1 -H "Authorization: Bearer <token>"
   ```
4. Open the Jaeger UI at http://localhost:16686 and find the trace spanning Gateway → Basket → Redis.

If any of these fail, start with [Troubleshooting](Troubleshooting).

## Running the tests

```bash
cd auth-microservice      && dotnet test
cd basket-microservice    && dotnet test
cd order-microservice     && dotnet test
cd product-microservice   && dotnet test
cd inventory-microservice && dotnet test
cd shipping-microservice  && dotnet test
cd payment-microservice   && dotnet test
cd api-gateway            && dotnet test
```

Integration tests provision isolated SQL Server databases via `WebApplicationFactory<Program>` and clean up via `IAsyncLifetime`. See [Testing](Testing).

## Where to go next

- [Architecture](Architecture) — how the pieces fit together
- [Integration-Events](Integration-Events) — what flows through RabbitMQ
- [Contributing](Contributing) — coding conventions before your first PR
