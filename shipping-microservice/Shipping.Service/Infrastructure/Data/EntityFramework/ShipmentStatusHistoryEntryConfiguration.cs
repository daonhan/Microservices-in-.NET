using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Shipping.Service.Models;

namespace Shipping.Service.Infrastructure.Data.EntityFramework;

internal class ShipmentStatusHistoryEntryConfiguration : IEntityTypeConfiguration<ShipmentStatusHistoryEntry>
{
    public void Configure(EntityTypeBuilder<ShipmentStatusHistoryEntry> builder)
    {
        builder.ToTable("ShipmentStatusHistory");

        builder.HasKey(h => h.Id);

        builder.Property(h => h.Status)
            .HasConversion<int>();

        builder.Property(h => h.Source)
            .HasConversion<int>();

        builder.Property(h => h.Reason)
            .HasMaxLength(500);

        builder.HasIndex(h => h.ShipmentId);

        builder.HasData(
            new ShipmentStatusHistoryEntry
            {
                Id = ShippingQaFixtures.HistorySeedIdStart,
                ShipmentId = ShippingQaFixtures.ShipmentPickPendingId,
                Status = ShipmentStatus.Pending,
                OccurredAt = ShippingQaFixtures.SeedEpoch,
                Source = ShipmentStatusSource.System,
            },
            new ShipmentStatusHistoryEntry
            {
                Id = ShippingQaFixtures.HistorySeedIdStart + 1,
                ShipmentId = ShippingQaFixtures.ShipmentPickedId,
                Status = ShipmentStatus.Picked,
                OccurredAt = ShippingQaFixtures.SeedEpoch,
                Source = ShipmentStatusSource.System,
            },
            new ShipmentStatusHistoryEntry
            {
                Id = ShippingQaFixtures.HistorySeedIdStart + 2,
                ShipmentId = ShippingQaFixtures.ShipmentPackedId,
                Status = ShipmentStatus.Packed,
                OccurredAt = ShippingQaFixtures.SeedEpoch,
                Source = ShipmentStatusSource.System,
            },
            new ShipmentStatusHistoryEntry
            {
                Id = ShippingQaFixtures.HistorySeedIdStart + 3,
                ShipmentId = ShippingQaFixtures.ShipmentDispatchedId,
                Status = ShipmentStatus.Shipped,
                OccurredAt = ShippingQaFixtures.SeedEpoch,
                Source = ShipmentStatusSource.System,
            },
            new ShipmentStatusHistoryEntry
            {
                Id = ShippingQaFixtures.HistorySeedIdStart + 4,
                ShipmentId = ShippingQaFixtures.ShipmentCancelPendingId,
                Status = ShipmentStatus.Pending,
                OccurredAt = ShippingQaFixtures.SeedEpoch,
                Source = ShipmentStatusSource.System,
            });
    }
}
