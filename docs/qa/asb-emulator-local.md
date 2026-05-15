# ASB Emulator Local Profile

Use this only when you want to exercise the Azure Service Bus provider locally. RabbitMQ remains the default for `docker compose up`, local Bruno smoke runs, and the Phase-4 smoke regression path.

## Start

Start only the emulator infrastructure:

```powershell
$env:ASB_EMULATOR_SQL_PASSWORD = "<strong local SQL password>"
docker compose --profile asb up -d servicebus-emulator servicebus-sql
```

To run the emulator beside the full default stack:

```powershell
$env:ASB_EMULATOR_SQL_PASSWORD = "<strong local SQL password>"
docker compose --profile asb up -d
```

RabbitMQ already owns host port `5672` in this repo, so the emulator maps host AMQP to `5673` by default while the container still listens on `5672`. Override only when the host port is free:

```powershell
$env:ASB_EMULATOR_AMQP_PORT = "5672"
$env:ASB_EMULATOR_HTTP_PORT = "5300"
$env:ASB_EMULATOR_SQL_PASSWORD = "<strong local SQL password>"
docker compose --profile asb up -d servicebus-emulator servicebus-sql
```

`ASB_EMULATOR_SQL_PASSWORD` is required for both the emulator and its SQL sidecar. Keep it in your shell or a local `.env` file and do not commit the value.

## Verify Health

```powershell
Invoke-WebRequest http://localhost:5300/health -UseBasicParsing
```

## Configure Host F5 Runs

Use these values when running a service from the host with `dotnet run` or F5:

```powershell
$env:Messaging__Provider = "AzureServiceBus"
$env:AzureServiceBus__ConnectionString = "Endpoint=sb://localhost:5673;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=SAS_KEY_VALUE;UseDevelopmentEmulator=true;"
$env:AzureServiceBus__AdministrationConnectionString = "Endpoint=sb://localhost:5300;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=SAS_KEY_VALUE;UseDevelopmentEmulator=true;"
$env:AzureServiceBus__TopicName = "ecommerce-topic"
$env:AzureServiceBus__AutoProvisionTopology = "Auto"
$env:EventBus__QueueName = "order-microservice"
```

`AzureServiceBus__ConnectionString` is the data-plane AMQP endpoint. `AzureServiceBus__AdministrationConnectionString` is used only by topology provisioning and should point at the emulator HTTP/management port. Keep both values separate so publishing still uses the broker port while topic and subscription checks use the administration port.

`Auto` provisions only when the connection string contains `UseDevelopmentEmulator=true`. Set `Always` only for a deliberate manual check against a namespace you are allowed to mutate. Set `Never` to verify startup without topology creation.

## Configure Compose Service Containers

The default `docker-compose.yaml` does not set `Messaging__Provider`, so services stay on RabbitMQ unless you explicitly override them. For a temporary Compose-based ASB run, add these environment values only to the service containers you are testing:

```yaml
environment:
  - "Messaging__Provider=AzureServiceBus"
  - "AzureServiceBus__ConnectionString=Endpoint=sb://servicebus-emulator;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=SAS_KEY_VALUE;UseDevelopmentEmulator=true;"
  - "AzureServiceBus__AdministrationConnectionString=Endpoint=sb://servicebus-emulator:5300;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=SAS_KEY_VALUE;UseDevelopmentEmulator=true;"
  - "AzureServiceBus__TopicName=ecommerce-topic"
  - "AzureServiceBus__AutoProvisionTopology=Auto"
```

The container data-plane connection uses `servicebus-emulator` without a port because the emulator listens on AMQP `5672` inside the Compose network. The administration connection includes `:5300` because topology provisioning talks to the emulator management endpoint.

## Run Opt-In Emulator Tests

Normal `dotnet test` skips emulator-backed tests. To run the publish/subscribe check against a running emulator:

```powershell
cd shared-libs
$env:ASB_EMULATOR_TESTS = "true"
$env:ASB_EMULATOR_CONNECTION_STRING = "Endpoint=sb://localhost:5673;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=SAS_KEY_VALUE;UseDevelopmentEmulator=true;"
$env:ASB_EMULATOR_ADMINISTRATION_CONNECTION_STRING = "Endpoint=sb://localhost:5300;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=SAS_KEY_VALUE;UseDevelopmentEmulator=true;"

dotnet test --filter "FullyQualifiedName~Given_ASB_emulator"
```

The opt-in gate is `ASB_EMULATOR_TESTS=true`. This path proves the shared ASB adapter can ensure `ecommerce-topic`, create a fresh subscription, publish an integration event, and receive it from that subscription. It is intentionally opt-in so CI and the Phase-4 smoke path remain RabbitMQ-only.

Clear the test variables when finished:

```powershell
Remove-Item Env:ASB_EMULATOR_TESTS -ErrorAction SilentlyContinue
Remove-Item Env:ASB_EMULATOR_CONNECTION_STRING -ErrorAction SilentlyContinue
Remove-Item Env:ASB_EMULATOR_ADMINISTRATION_CONNECTION_STRING -ErrorAction SilentlyContinue
```

## Verify Provider-Agnostic DLQ

Gateway ASB DLQ capture uses the same emulator connection strings plus the configured subscription list from `api-gateway/ApiGateway/appsettings.json`:

| Subscription | Source service config |
|---|---|
| `basket-microservice` | `Basket.Service` `EventBus:QueueName` |
| `order-microservice` | `Order.Service` `EventBus:QueueName` |
| `inventory-microservice` | `Inventory.Service` `EventBus:QueueName` |
| `payment-microservice` | `Payment.Service` `EventBus:QueueName` |
| `shipping-microservice` | `Shipping.Service` `EventBus:QueueName` |

The gateway operator endpoints and `dead_letter_messages` table do not change between RabbitMQ and ASB. Replay and discard still go through `/operator/api/failures*`; only the capture/replay publisher behind `IDeadLetterCapture` and `IDeadLetterPublisher` changes with `Messaging__Provider`.

If the emulator or one configured subscription is unavailable, the ASB capture processor logs a warning and the gateway keeps running. If persisting a received dead-letter message fails, the message is abandoned so the broker can redeliver it.

## Real Azure Namespaces

Real Azure topology remains Bicep-owned. With `AzureServiceBus__AutoProvisionTopology=Auto`, non-emulator connection strings are detected as cloud namespaces and topology provisioning is skipped. Production, staging, and shared dev namespaces should get topics and subscriptions from the infrastructure deployment rather than application startup.

## Teardown

This resets the whole local Compose project, including emulator containers and volumes:

```powershell
docker compose --profile asb down -v --remove-orphans
```

For an emulator-only reset without stopping the rest of the stack:

```powershell
docker compose --profile asb stop servicebus-emulator servicebus-sql
docker compose --profile asb rm -f servicebus-emulator servicebus-sql
docker volume ls -q --filter name=servicebus-sql-data | ForEach-Object { docker volume rm $_ }
```
