using ECommerce.Shared.Qa;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Shipping.Service.Domain;

namespace Shipping.Service.Infrastructure.Data.EntityFramework;

internal class ShipmentLineConfiguration : IEntityTypeConfiguration<ShipmentLine>
{
    public void Configure(EntityTypeBuilder<ShipmentLine> builder)
    {
        builder.HasKey(l => l.Id);

        builder.HasIndex(l => l.ShipmentId);

        builder.HasData(
            new ShipmentLine
            {
                Id = ShippingQaFixtures.LineSeedIdStart,
                ShipmentId = ShippingQaFixtures.ShipmentPickPendingId,
                ProductId = QaPersonas.ProductHappyId,
                Quantity = QaPersonas.ProductHappyQuantity,
            },
            new ShipmentLine
            {
                Id = ShippingQaFixtures.LineSeedIdStart + 1,
                ShipmentId = ShippingQaFixtures.ShipmentPickedId,
                ProductId = QaPersonas.ProductHappyId,
                Quantity = QaPersonas.ProductHappyQuantity,
            },
            new ShipmentLine
            {
                Id = ShippingQaFixtures.LineSeedIdStart + 2,
                ShipmentId = ShippingQaFixtures.ShipmentPackedId,
                ProductId = QaPersonas.ProductHappyId,
                Quantity = QaPersonas.ProductHappyQuantity,
            },
            new ShipmentLine
            {
                Id = ShippingQaFixtures.LineSeedIdStart + 3,
                ShipmentId = ShippingQaFixtures.ShipmentDispatchedId,
                ProductId = QaPersonas.ProductHappyId,
                Quantity = QaPersonas.ProductHappyQuantity,
            },
            new ShipmentLine
            {
                Id = ShippingQaFixtures.LineSeedIdStart + 4,
                ShipmentId = ShippingQaFixtures.ShipmentCancelPendingId,
                ProductId = QaPersonas.ProductHappyId,
                Quantity = QaPersonas.ProductHappyQuantity,
            },
            new ShipmentLine
            {
                Id = ShippingQaFixtures.LineSeedIdStart + 5,
                ShipmentId = ShippingQaFixtures.ShipmentFailDispatchedId,
                ProductId = QaPersonas.ProductHappyId,
                Quantity = QaPersonas.ProductHappyQuantity,
            },
            new ShipmentLine
            {
                Id = ShippingQaFixtures.LineSeedIdStart + 6,
                ShipmentId = ShippingQaFixtures.ShipmentReturnDispatchedId,
                ProductId = QaPersonas.ProductHappyId,
                Quantity = QaPersonas.ProductHappyQuantity,
            });
    }
}
