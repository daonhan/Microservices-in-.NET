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
    }
}
