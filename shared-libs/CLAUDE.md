# Shared libraries — notes

`ECommerce.Shared` is consumed as a NuGet package (not project ref). Local feed: `local-nuget-packages/` (gitignored).

## Pack + publish flow

After edits:

```bash
cd shared-libs/ECommerce.Shared
dotnet pack -c Release
dotnet nuget push bin/Release/*.nupkg -s ../../local-nuget-packages
# bump <Version> in .csproj so consumers pick it up
```

Consumers see no change until version bump + new `.nupkg` in feed.

When packing a new shared version, also confirm the `.nupkg` in `local-nuget-packages/` was built **after** the relevant source commit (older nupkgs sharing a version number have been observed).

## Composition extensions

Each service's `Program.cs` uses `ECommerce.Shared` extensions: `AddSqlServerDatastore`, `AddOutbox`, `AddPlatformEventBus`, `AddPlatformEventPublisher`, `AddPlatformSubscriberService`, `AddEventHandler<TEvent,THandler>`, `AddPlatformObservability`, `AddPlatformHealthChecks`, `AddPlatformOpenApi`.

`Infrastructure/`:
- `EventBus/`, `Messaging/`, `RabbitMq/`, `AzureServiceBus/` — `Messaging:Provider` selects RabbitMQ by default or Azure Service Bus.
- `Outbox/` — `OutboxBackgroundService`. Services that publish need `AddOutbox(...)` + `app.ApplyOutboxMigrations()` in Dev.

New cross-cutting concerns belong here.

## Broker singletons must register lazy

```csharp
// good
AddSingleton<IRabbitMqConnection>(_ => new RabbitMqConnection(opts));

// bad — opens socket during Program.Main, before WebApplicationFactory.ConfigureWebHost can swap stubs
AddSingleton<IRabbitMqConnection>(new RabbitMqConnection(opts));
```

Eager registration breaks boot tests like `Inventory.Tests.MessagingProviderBootTests` in any sandbox without a reachable broker.

## Version pinning history

- Lazy broker fix shipped in `ECommerce.Shared` ≥ 2.25.0 (commit `dcbc29c`).
- **Inventory** pins 2.25.0.
- **Other services** still pin 2.23.0 / 2.18.0 and carry the latent eager defect — sweeping them is a separate ADR/PR.
