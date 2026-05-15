# Local Messaging Development

This is the canonical local-dev entry point for choosing the messaging provider. RabbitMQ is the default for `docker compose up`, `dotnet run`, and normal tests. Azure Service Bus is opt-in through `Messaging__Provider=AzureServiceBus`.

Use [ASB Emulator Local Profile](../qa/asb-emulator-local.md) for the emulator-only health check, opt-in adapter test, DLQ verification, and teardown procedure.

## Choose A Scenario

| Scenario | Use it when | Trade-off |
|---|---|---|
| Default Compose Rabbit | You want the full stack running with the default local broker. | Fastest path and matches local smoke expectations, but does not exercise the ASB adapter. |
| F5 + ASB emulator | You want to debug one or more services against ASB without an Azure subscription. | Offline and quick feedback, but emulator coverage is not full cloud Service Bus fidelity. |
| F5 + shared dev namespace | You need real ASB behavior, managed identities, or cloud networking behavior. | Highest fidelity, but it costs money, requires secrets, and topology should already exist. |
| Compose `--profile asb` | You want service containers to talk to the local emulator. | Closer to containerized local flow, but heavier than F5 and still requires explicit provider overrides. |

Queue names should match each service's `EventBus:QueueName` value. Current subscriber queue names are:

| Service | `EventBus__QueueName` |
|---|---|
| Basket | `basket-microservice` |
| Order | `order-microservice` |
| Inventory | `inventory-microservice` |
| Payment | `payment-microservice` |
| Shipping | `shipping-microservice` |

Product uses `product-microservice` when a queue name is needed for provider boot checks. Auth does not consume integration events.

## Scenario 1: Default Compose Rabbit

Choose this for day-to-day local development and the clean-clone smoke path.

```powershell
docker compose up --build
```

Provider settings are already supplied by committed `appsettings.json` defaults and Compose environment values:

```text
Messaging__Provider=RabbitMq
RabbitMq__HostName=host.docker.internal
EventBus__QueueName=<service queue name, for example order-microservice>
```

Do not set these ASB values for the Rabbit path:

```text
AzureServiceBus__ConnectionString=
AzureServiceBus__AdministrationConnectionString=
AzureServiceBus__TopicName=
AzureServiceBus__AutoProvisionTopology=
```

Minimum smoke from a clean clone:

```powershell
docker compose ps
Invoke-WebRequest http://localhost:8004/health/live -UseBasicParsing
Invoke-WebRequest http://localhost:8004/health/ready -UseBasicParsing
Invoke-WebRequest http://localhost:15672 -UseBasicParsing
```

Then open `http://localhost:8004/swagger` for the gateway-routed API surface and `http://localhost:15672` for RabbitMQ Management (`guest` / `guest`).

## Scenario 2: F5 + ASB Emulator

Choose this when you want to debug a service from the host while using the local ASB emulator.

Start the emulator infrastructure:

```powershell
docker compose --profile asb up -d servicebus-emulator servicebus-sql
```

Use these environment variables for each host-run service:

```text
Messaging__Provider=AzureServiceBus
AzureServiceBus__ConnectionString=Endpoint=sb://localhost:5673;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=SAS_KEY_VALUE;UseDevelopmentEmulator=true;
AzureServiceBus__AdministrationConnectionString=Endpoint=sb://localhost:5300;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=SAS_KEY_VALUE;UseDevelopmentEmulator=true;
AzureServiceBus__TopicName=ecommerce-topic
AzureServiceBus__AutoProvisionTopology=Auto
EventBus__QueueName=order-microservice
```

For F5, the same values can live in a local-only `appsettings.Development.json` override:

```json
{
  "Messaging": {
    "Provider": "AzureServiceBus"
  },
  "AzureServiceBus": {
    "ConnectionString": "Endpoint=sb://localhost:5673;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=SAS_KEY_VALUE;UseDevelopmentEmulator=true;",
    "AdministrationConnectionString": "Endpoint=sb://localhost:5300;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=SAS_KEY_VALUE;UseDevelopmentEmulator=true;",
    "TopicName": "ecommerce-topic",
    "AutoProvisionTopology": "Auto"
  },
  "EventBus": {
    "QueueName": "order-microservice"
  }
}
```

Replace `order-microservice` with the service you are debugging. `Auto` provisions the topic and that service's subscription only when the connection string contains `UseDevelopmentEmulator=true`.

The emulator QA guide has the host-port notes, health check, opt-in ASB adapter test, DLQ verification, and teardown: [docs/qa/asb-emulator-local.md](../qa/asb-emulator-local.md).

## Scenario 3: F5 + Shared Dev Namespace

Choose this when emulator behavior is not enough and you need a real Azure Service Bus namespace. Use repository or user secrets for actual connection strings; do not commit them.

Use these environment variables for each host-run service:

```text
Messaging__Provider=AzureServiceBus
AzureServiceBus__ConnectionString=Endpoint=sb://<namespace>.servicebus.windows.net/;SharedAccessKeyName=<data-key-name>;SharedAccessKey=<data-key>
AzureServiceBus__AdministrationConnectionString=Endpoint=sb://<namespace>.servicebus.windows.net/;SharedAccessKeyName=<manage-key-name>;SharedAccessKey=<manage-key>
AzureServiceBus__TopicName=ecommerce-topic
AzureServiceBus__AutoProvisionTopology=Never
EventBus__QueueName=order-microservice
```

Use this `appsettings.Development.json` shape only with local secret replacement:

```json
{
  "Messaging": {
    "Provider": "AzureServiceBus"
  },
  "AzureServiceBus": {
    "ConnectionString": "<secret data-plane connection string>",
    "AdministrationConnectionString": "<secret management connection string>",
    "TopicName": "ecommerce-topic",
    "AutoProvisionTopology": "Never"
  },
  "EventBus": {
    "QueueName": "order-microservice"
  }
}
```

`Never` is the safest default for a shared namespace because Azure topology is owned by infrastructure deployment. Use `Always` only for an explicit topology check against a namespace you are allowed to mutate. With `Auto`, non-emulator connection strings skip topology provisioning.

## Scenario 4: Compose `--profile asb`

Choose this when service containers, not host-run services, should talk to the ASB emulator. The profile starts the emulator and its SQL sidecar, but it does not switch application services away from RabbitMQ by itself.

Start the emulator beside the default stack:

```powershell
docker compose --profile asb up -d
```

For a temporary ASB container run, add provider overrides only to the service containers you are testing:

```yaml
services:
  order:
    environment:
      - "Messaging__Provider=AzureServiceBus"
      - "AzureServiceBus__ConnectionString=Endpoint=sb://servicebus-emulator;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=SAS_KEY_VALUE;UseDevelopmentEmulator=true;"
      - "AzureServiceBus__AdministrationConnectionString=Endpoint=sb://servicebus-emulator:5300;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=SAS_KEY_VALUE;UseDevelopmentEmulator=true;"
      - "AzureServiceBus__TopicName=ecommerce-topic"
      - "AzureServiceBus__AutoProvisionTopology=Auto"
      - "EventBus__QueueName=order-microservice"
```

Inside the Compose network, `AzureServiceBus__ConnectionString` uses `servicebus-emulator` without a port because the emulator listens on AMQP `5672` in the container. The administration connection uses `servicebus-emulator:5300` for topology checks.

Keep these overrides local to the run. The committed default Compose environment stays RabbitMQ-first.
