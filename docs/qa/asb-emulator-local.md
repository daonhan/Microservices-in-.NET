# ASB Emulator Local Profile

Use this only when you want to exercise the Azure Service Bus provider locally. The default stack remains RabbitMQ-first.

## Start

```powershell
docker compose --profile asb up -d servicebus-emulator servicebus-sql
```

To run the emulator beside the full default stack:

```powershell
docker compose --profile asb up -d
```

RabbitMQ already owns host port `5672` in this repo, so the emulator maps host AMQP to `5673` by default while the container still listens on `5672`. Override only when the host port is free:

```powershell
$env:ASB_EMULATOR_AMQP_PORT = "5672"
$env:ASB_EMULATOR_HTTP_PORT = "5300"
docker compose --profile asb up -d servicebus-emulator servicebus-sql
```

## Verify

```powershell
Invoke-WebRequest http://localhost:5300/health -UseBasicParsing
```

Host-run connection string:

```text
Endpoint=sb://localhost:5673;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=SAS_KEY_VALUE;UseDevelopmentEmulator=true;
```

Administration operations use the emulator HTTP port:

```text
Endpoint=sb://localhost:5300;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=SAS_KEY_VALUE;UseDevelopmentEmulator=true;
```

Containers on the Compose network can use the service name:

```text
Endpoint=sb://servicebus-emulator;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=SAS_KEY_VALUE;UseDevelopmentEmulator=true;
```

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
