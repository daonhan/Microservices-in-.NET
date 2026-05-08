using ECommerce.Shared.Qa;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Shipping.Service.Models;

namespace Shipping.Service.Infrastructure.Data.EntityFramework;

internal class ShipmentConfiguration : IEntityTypeConfiguration<Shipment>
{
    public void Configure(EntityTypeBuilder<Shipment> builder)
    {
        builder.HasKey(s => s.Id);

        builder.Property(s => s.CustomerId)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(s => s.Status)
            .HasConversion<int>();

        builder.Property(s => s.CarrierKey)
            .HasMaxLength(50);

        builder.Property(s => s.TrackingNumber)
            .HasMaxLength(100);

        builder.Property(s => s.LabelRef)
            .HasMaxLength(500);

        builder.Property(s => s.QuotedPriceAmount)
            .HasColumnType("decimal(18,2)");

        builder.Property(s => s.QuotedPriceCurrency)
            .HasMaxLength(3);

        builder.OwnsOne(s => s.ShippingAddress, address =>
        {
            address.Property(a => a.Recipient).HasColumnName("ShippingAddress_Recipient").HasMaxLength(200);
            address.Property(a => a.Line1).HasColumnName("ShippingAddress_Line1").HasMaxLength(200);
            address.Property(a => a.Line2).HasColumnName("ShippingAddress_Line2").HasMaxLength(200);
            address.Property(a => a.City).HasColumnName("ShippingAddress_City").HasMaxLength(100);
            address.Property(a => a.State).HasColumnName("ShippingAddress_State").HasMaxLength(100);
            address.Property(a => a.PostalCode).HasColumnName("ShippingAddress_PostalCode").HasMaxLength(20);
            address.Property(a => a.Country).HasColumnName("ShippingAddress_Country").HasMaxLength(2);
        });

        builder.HasIndex(s => s.OrderId);

        builder.HasMany(s => s.Lines)
            .WithOne()
            .HasForeignKey(l => l.ShipmentId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(s => s.StatusHistory)
            .WithOne()
            .HasForeignKey(h => h.ShipmentId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Metadata.FindNavigation(nameof(Shipment.Lines))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);

        builder.Metadata.FindNavigation(nameof(Shipment.StatusHistory))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);

        var customerHappyId = QaPersonas.CustomerHappyId.ToString();
        builder.HasData(
            new
            {
                Id = ShippingQaFixtures.ShipmentPickPendingId,
                OrderId = ShippingQaFixtures.ShippingOrderPickPendingId,
                CustomerId = customerHappyId,
                WarehouseId = QaPersonas.DefaultWarehouseId,
                Status = ShipmentStatus.Pending,
                CreatedAt = ShippingQaFixtures.SeedEpoch,
            },
            new
            {
                Id = ShippingQaFixtures.ShipmentPickedId,
                OrderId = ShippingQaFixtures.ShippingOrderPickedId,
                CustomerId = customerHappyId,
                WarehouseId = QaPersonas.DefaultWarehouseId,
                Status = ShipmentStatus.Picked,
                CreatedAt = ShippingQaFixtures.SeedEpoch,
            },
            new
            {
                Id = ShippingQaFixtures.ShipmentPackedId,
                OrderId = ShippingQaFixtures.ShippingOrderPackedId,
                CustomerId = customerHappyId,
                WarehouseId = QaPersonas.DefaultWarehouseId,
                Status = ShipmentStatus.Packed,
                CreatedAt = ShippingQaFixtures.SeedEpoch,
            },
            new
            {
                Id = ShippingQaFixtures.ShipmentDispatchedId,
                OrderId = ShippingQaFixtures.ShippingOrderDispatchedId,
                CustomerId = customerHappyId,
                WarehouseId = QaPersonas.DefaultWarehouseId,
                Status = ShipmentStatus.Shipped,
                CreatedAt = ShippingQaFixtures.SeedEpoch,
                CarrierKey = ShippingQaFixtures.DispatchedCarrierKey,
                TrackingNumber = ShippingQaFixtures.DispatchedTrackingNumber,
                LabelRef = ShippingQaFixtures.DispatchedLabelRef,
                QuotedPriceAmount = (decimal?)ShippingQaFixtures.DispatchedQuotedAmount,
                QuotedPriceCurrency = ShippingQaFixtures.DispatchedQuotedCurrency,
            },
            new
            {
                Id = ShippingQaFixtures.ShipmentCancelPendingId,
                OrderId = ShippingQaFixtures.ShippingOrderCancelPendingId,
                CustomerId = customerHappyId,
                WarehouseId = QaPersonas.DefaultWarehouseId,
                Status = ShipmentStatus.Pending,
                CreatedAt = ShippingQaFixtures.SeedEpoch,
            });
    }
}
