using Microsoft.Extensions.DependencyInjection;
using Payment.Service.Contracts.Integration;
using Payment.Service.Features.OrderCreated;

namespace Payment.Tests.Features.OrderCreated;

public class OrderCreatedHandlerTests : IntegrationTestBase
{
    public OrderCreatedHandlerTests(PaymentWebApplicationFactory factory)
        : base(factory)
    {
    }

    [Fact]
    public async Task Given_OrderCreatedEvent_When_Handled_Then_RecordsOrderCustomer()
    {
        var orderId = Guid.NewGuid();
        var customerId = $"cust-{Guid.NewGuid():N}";
        var @event = new OrderCreatedEvent(
            orderId,
            customerId,
            Items: [new OrderItem("sku-1", 1, 10m)]);

        using var scope = Factory.Services.CreateScope();
        var handler = ActivatorUtilities.CreateInstance<OrderCreatedHandler>(scope.ServiceProvider);

        await handler.Handle(@event);

        PaymentContext.ChangeTracker.Clear();
        var record = PaymentContext.OrderCustomers.Single(oc => oc.OrderId == orderId);
        Assert.Equal(customerId, record.CustomerId);
    }
}
