using System.Globalization;
using ECommerce.Shared.Qa;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Order.Service.Domain;
using Order.Service.Infrastructure.Data.EntityFramework;

namespace Order.Tests.Api;

public class QaSeedPresenceTests : IntegrationTestBase
{
    public QaSeedPresenceTests(OrderWebApplicationFactory webApplicationFactory)
        : base(webApplicationFactory)
    {
    }

    [Fact]
    public async Task QaSeeds_AuthorizedOrder_IsPresentAndCorrect()
    {
        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<OrderContext>();

        var order = await context.Orders
            .Include(o => o.OrderProducts)
            .SingleOrDefaultAsync(o => o.OrderId == QaPersonas.OrderAuthorizedId);

        Assert.NotNull(order);
        Assert.Equal(QaPersonas.CustomerHappyId.ToString(), order.CustomerId);
        Assert.Equal(OrderStatus.Confirmed, order.Status);

        Assert.Single(order.OrderProducts);
        var product = order.OrderProducts.First();
        Assert.Equal(QaPersonas.OrderProductAuthorizedId, product.Id);
        Assert.Equal(QaPersonas.ProductHappyId.ToString(CultureInfo.InvariantCulture), product.ProductId);
        Assert.Equal(QaPersonas.ProductHappyQuantity, product.Quantity);
    }

    [Fact]
    public async Task QaSeeds_CapturedOrder_IsPresentAndCorrect()
    {
        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<OrderContext>();

        var order = await context.Orders
            .Include(o => o.OrderProducts)
            .SingleOrDefaultAsync(o => o.OrderId == QaPersonas.OrderCapturedId);

        Assert.NotNull(order);
        Assert.Equal(QaPersonas.CustomerHappyId.ToString(), order.CustomerId);
        Assert.Equal(OrderStatus.Confirmed, order.Status);

        Assert.Single(order.OrderProducts);
        var product = order.OrderProducts.First();
        Assert.Equal(QaPersonas.OrderProductCapturedId, product.Id);
        Assert.Equal(QaPersonas.ProductHappyId.ToString(CultureInfo.InvariantCulture), product.ProductId);
        Assert.Equal(QaPersonas.ProductHappyQuantity, product.Quantity);
    }
}
