# Adding a new slice (Order service, post-VSA pilot)

This runbook is the step-by-step walkthrough for adding a new feature to `Order.Service` under the layout established by [ADR-0011](../adr/0011-order-cleanarch-vsa-pilot.md). Use it for any new inbound trigger — HTTP route, integration-event consumer, or saga command consumer.

Existing slices to mirror as templates:

- HTTP write slice: [`Features/CreateOrder/`](../../order-microservice/Order.Service/Features/CreateOrder/)
- HTTP read slice: [`Features/GetOrder/`](../../order-microservice/Order.Service/Features/GetOrder/), [`Features/ListOrders/`](../../order-microservice/Order.Service/Features/ListOrders/)
- Integration-event consumer: [`Features/ProductCreated/`](../../order-microservice/Order.Service/Features/ProductCreated/)
- Saga command consumer: [`Features/ConfirmOrder/`](../../order-microservice/Order.Service/Features/ConfirmOrder/), [`Features/CancelOrder/`](../../order-microservice/Order.Service/Features/CancelOrder/)

## 1. Choose the slice name

The slice name is the inbound trigger expressed in one PascalCase phrase. Examples: `CreateOrder` (HTTP POST), `GetOrder` (HTTP GET), `ConfirmOrder` (saga command), `ProductCreated` (integration event).

One inbound trigger = one slice. Do not bundle two routes into one slice "because they are about the same aggregate". The whole point of the layout is one folder per trigger.

## 2. Scaffold the folder

Create `order-microservice/Order.Service/Features/<Slice>/`. The folder owns:

- **Endpoint or consumer**: `<Slice>Endpoint.cs` (HTTP) or `<Slice>EventHandler.cs` / `<Slice>CommandHandler.cs` (event/command).
- **Request DTO** (HTTP write only): `<Slice>Request.cs`. Read slices return projections; consumers receive the integration-event payload directly.
- **Response DTO**: `<Slice>Response.cs` (HTTP read only — write endpoints currently return `TypedResults.Created` and don't need a separate response type).
- **Handler**: `<Slice>Handler.cs`, `internal sealed`, one public async method (typically `HandleAsync`).
- **Slice DI extension**: `<Slice>SliceExtensions.cs`, `internal static class`, one public method `Add<Slice>Slice(this IServiceCollection)`.
- **Integration map** (only if the slice publishes an integration event): `<EventName>IntegrationMap.cs`, `internal sealed class` implementing `IIntegrationMap<TDomainEvent, TIntegrationEvent>`.

Namespace must match the folder: `namespace Order.Service.Features.<Slice>;`.

## 3. Write the handler

Handler is orchestration only. Business rules live on the `Order` aggregate; persistence lives behind `IOrderStore`. Keep the constructor narrow — inject only what the slice needs.

```csharp
internal sealed class <Slice>Handler
{
    private readonly IOrderStore _orderStore;

    public <Slice>Handler(IOrderStore orderStore) => _orderStore = orderStore;

    public async Task<...> HandleAsync(...)
    {
        // 1. Load aggregate (write) OR project (read).
        // 2. Call domain method (write only) — invariants live on the aggregate.
        // 3. Persist (write) via _orderStore.ExecuteAsync(...).
        // 4. Return DTO or domain object.
    }
}
```

For read slices, inject `OrderContext` directly and `.Select(...)` straight into the response DTO. Do not hydrate the aggregate.

## 4. Write the endpoint or consumer

HTTP endpoint:

```csharp
internal static class <Slice>Endpoint
{
    public static IEndpointRouteBuilder Map<Slice>(this IEndpointRouteBuilder routes)
    {
        routes.MapPost("/...", HandleAsync);
        return routes;
    }

    internal static async Task<IResult> HandleAsync(<Slice>Handler handler, ...)
    {
        // call handler, return TypedResults.*
    }
}
```

Integration-event or saga-command consumer: implement `IEventHandler<TEvent>` (from `ECommerce.Shared.Infrastructure.EventBus`) and delegate to the slice handler.

## 5. Add the integration map (only if the slice publishes)

If the slice mutates the aggregate and the aggregate raises a domain event the rest of the platform cares about, ship the mapper inside the slice:

```csharp
internal sealed class <Event>IntegrationMap : IIntegrationMap<<DomainEvent>, <IntegrationEvent>>
{
    public Type DomainEventType => typeof(<DomainEvent>);

    public <IntegrationEvent> Map(<DomainEvent> e) => new(...);

    Event IIntegrationMap.Map(IDomainEvent e) => Map((<DomainEvent>)e);
}
```

Register it in the slice DI extension (step 6). The `DomainEventOutboxInterceptor` resolves it by runtime type; no central switch to edit.

## 6. Write the slice DI extension

```csharp
internal static class <Slice>SliceExtensions
{
    public static IServiceCollection Add<Slice>Slice(this IServiceCollection services)
    {
        services.AddScoped<<Slice>Handler>();
        // If publishing:    services.AddScoped<IIntegrationMap, <Event>IntegrationMap>();
        // If event/command: services.AddEventHandler<<Event>, <Slice>EventHandler>();
        return services;
    }
}
```

Keep this method short. It is the slice's complete DI contract.

## 7. Register the slice from `Program.cs`

Two lines in [`Program.cs`](../../order-microservice/Order.Service/Program.cs):

1. Add `builder.Services.Add<Slice>Slice();` to the slice-registration block.
2. For HTTP slices only, add `app.Map<Slice>();` to the endpoint-mapping block.

`Program.cs` is the manifest; the order of slice extensions there reflects the order new contributors should read the codebase.

## 8. Respect the cross-slice rule

`Order.Service.Features.<Slice>` may **not** reference any other `Order.Service.Features.<OtherSlice>`. NetArchTest and the Roslyn `LayoutAnalyzer` will both fail the build.

If you find yourself wanting to call into another slice, copy the code instead. On the **third** duplicate, extract — into `Domain/` if the logic is behavioral, into `Features/Shared/` only if it is a pure helper.

## 9. Mirror tests into `Order.Tests/Features/<Slice>/`

Test layout mirrors the source layout:

- HTTP slices: `Order.Tests/Features/<Slice>/<Slice>EndpointTests.cs`, using `OrderWebApplicationFactory` + `IntegrationTestBase`.
- Event/command consumers: `Order.Tests/Features/<Slice>/<Slice>HandlerTests.cs`.
- Integration map: `Order.Tests/Features/<Slice>/<Event>IntegrationMapTests.cs` — small pure-function tests asserting field-level mapping.

Test display names use the `Given_When_Then` underscore convention (`CA1707` is suppressed via `Directory.Build.props`).

Aggregate-level invariants stay in `Order.Tests/Domain/OrderTests.cs`. Do not duplicate aggregate tests into the slice.

## 10. Run the pre-commit gate

From the repo root:

```bash
cd order-microservice
dotnet build               # warnings are errors
dotnet test                # full Order suite (NetArchTest included)
dotnet format --verify-no-changes --verbosity minimal
```

Then commit. The pre-commit hook runs `dotnet format` + `dotnet build` + Basket tests. Per [root CLAUDE.md](../../CLAUDE.md), never bypass with `--no-verify`, `-c core.hooksPath=`, or `Hooks-Deferred:` footer — if the hook cannot pass in your environment, hand off rather than commit.

## Checklist before opening the PR

- [ ] Slice folder `Features/<Slice>/` contains endpoint/consumer, request/response DTOs as applicable, sealed handler, slice DI extension, and (if publishing) integration map.
- [ ] Slice is registered via `Add<Slice>Slice()` in `Program.cs`, and HTTP slices are mapped via `app.Map<Slice>()`.
- [ ] Namespace matches folder.
- [ ] No reference into another `Features.<OtherSlice>`.
- [ ] Tests mirror the slice under `Order.Tests/Features/<Slice>/`.
- [ ] `dotnet build`, `dotnet test`, `dotnet format --verify-no-changes` all clean.
- [ ] NetArchTest `LayoutTests` all green (no new `Skip`).
