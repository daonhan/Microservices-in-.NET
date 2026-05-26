using ECommerce.Shared.Qa;
using Microsoft.EntityFrameworkCore;
using Shipping.Service.Domain;
using Shipping.Service.Infrastructure.Data.EntityFramework;

namespace Shipping.Tests.Domain;

public class QaSeedFixturesTests : IntegrationTestBase
{
    public QaSeedFixturesTests(ShippingWebApplicationFactory webApplicationFactory)
        : base(webApplicationFactory)
    {
    }

    [Fact]
    public async Task Given_FreshDatabase_When_MigrationsApplied_Then_SevenShipmentFixturesArePresent()
    {
        var customerHappyId = QaPersonas.CustomerHappyId.ToString();

        var seeded = await ShippingContext.Shipments
            .Where(s => s.CustomerId == customerHappyId
                && new[]
                {
                    ShippingQaFixtures.ShipmentPickPendingId,
                    ShippingQaFixtures.ShipmentPickedId,
                    ShippingQaFixtures.ShipmentPackedId,
                    ShippingQaFixtures.ShipmentDispatchedId,
                    ShippingQaFixtures.ShipmentCancelPendingId,
                    ShippingQaFixtures.ShipmentFailDispatchedId,
                    ShippingQaFixtures.ShipmentReturnDispatchedId,
                }.Contains(s.Id))
            .ToDictionaryAsync(s => s.Id, s => s.Status);

        Assert.Equal(7, seeded.Count);
        Assert.Equal(ShipmentStatus.Pending, seeded[ShippingQaFixtures.ShipmentPickPendingId]);
        Assert.Equal(ShipmentStatus.Picked, seeded[ShippingQaFixtures.ShipmentPickedId]);
        Assert.Equal(ShipmentStatus.Packed, seeded[ShippingQaFixtures.ShipmentPackedId]);
        Assert.Equal(ShipmentStatus.Shipped, seeded[ShippingQaFixtures.ShipmentDispatchedId]);
        Assert.Equal(ShipmentStatus.Pending, seeded[ShippingQaFixtures.ShipmentCancelPendingId]);
        Assert.Equal(ShipmentStatus.Shipped, seeded[ShippingQaFixtures.ShipmentFailDispatchedId]);
        Assert.Equal(ShipmentStatus.Shipped, seeded[ShippingQaFixtures.ShipmentReturnDispatchedId]);
    }

    [Fact]
    public async Task Given_FreshDatabase_When_DispatchedFixtureLoaded_Then_CarrierFieldsAndTrackingNumberAreSet()
    {
        var dispatched = await ShippingContext.Shipments
            .AsNoTracking()
            .SingleAsync(s => s.Id == ShippingQaFixtures.ShipmentDispatchedId);

        Assert.Equal(ShippingQaFixtures.DispatchedCarrierKey, dispatched.CarrierKey);
        Assert.Equal(ShippingQaFixtures.DispatchedTrackingNumber, dispatched.TrackingNumber);
        Assert.Equal(ShippingQaFixtures.DispatchedLabelRef, dispatched.LabelRef);
        Assert.Equal(ShippingQaFixtures.DispatchedQuotedAmount, dispatched.QuotedPriceAmount);
        Assert.Equal(ShippingQaFixtures.DispatchedQuotedCurrency, dispatched.QuotedPriceCurrency);
    }

    [Fact]
    public async Task Given_FreshDatabase_When_FailDispatchedFixtureLoaded_Then_CarrierFieldsAndTrackingNumberAreSet()
    {
        var failDispatched = await ShippingContext.Shipments
            .AsNoTracking()
            .SingleAsync(s => s.Id == ShippingQaFixtures.ShipmentFailDispatchedId);

        Assert.Equal(ShippingQaFixtures.DispatchedCarrierKey, failDispatched.CarrierKey);
        Assert.Equal(ShippingQaFixtures.FailDispatchedTrackingNumber, failDispatched.TrackingNumber);
        Assert.Equal(ShippingQaFixtures.FailDispatchedLabelRef, failDispatched.LabelRef);
        Assert.Equal(ShippingQaFixtures.DispatchedQuotedAmount, failDispatched.QuotedPriceAmount);
        Assert.Equal(ShippingQaFixtures.DispatchedQuotedCurrency, failDispatched.QuotedPriceCurrency);
    }

    [Fact]
    public async Task Given_FreshDatabase_When_ReturnDispatchedFixtureLoaded_Then_CarrierFieldsAndTrackingNumberAreSet()
    {
        var returnDispatched = await ShippingContext.Shipments
            .AsNoTracking()
            .SingleAsync(s => s.Id == ShippingQaFixtures.ShipmentReturnDispatchedId);

        Assert.Equal(ShippingQaFixtures.DispatchedCarrierKey, returnDispatched.CarrierKey);
        Assert.Equal(ShippingQaFixtures.ReturnDispatchedTrackingNumber, returnDispatched.TrackingNumber);
        Assert.Equal(ShippingQaFixtures.ReturnDispatchedLabelRef, returnDispatched.LabelRef);
        Assert.Equal(ShippingQaFixtures.DispatchedQuotedAmount, returnDispatched.QuotedPriceAmount);
        Assert.Equal(ShippingQaFixtures.DispatchedQuotedCurrency, returnDispatched.QuotedPriceCurrency);
    }

    [Fact]
    public async Task Given_FreshDatabase_When_SeedShipmentsLoaded_Then_EachHasOneProductHappyLine()
    {
        var seedIds = new[]
        {
            ShippingQaFixtures.ShipmentPickPendingId,
            ShippingQaFixtures.ShipmentPickedId,
            ShippingQaFixtures.ShipmentPackedId,
            ShippingQaFixtures.ShipmentDispatchedId,
            ShippingQaFixtures.ShipmentCancelPendingId,
            ShippingQaFixtures.ShipmentFailDispatchedId,
            ShippingQaFixtures.ShipmentReturnDispatchedId,
        };

        var shipments = await ShippingContext.Shipments
            .AsNoTracking()
            .Include(s => s.Lines)
            .Where(s => seedIds.Contains(s.Id))
            .ToListAsync();

        Assert.Equal(7, shipments.Count);
        foreach (var shipment in shipments)
        {
            var line = Assert.Single(shipment.Lines);
            Assert.Equal(QaPersonas.ProductHappyId, line.ProductId);
            Assert.Equal(QaPersonas.ProductHappyQuantity, line.Quantity);
        }
    }
}
