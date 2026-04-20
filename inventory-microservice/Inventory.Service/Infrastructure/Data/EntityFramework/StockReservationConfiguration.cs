using Inventory.Service.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Inventory.Service.Infrastructure.Data.EntityFramework;

internal class StockReservationConfiguration : IEntityTypeConfiguration<StockReservation>
{
    public void Configure(EntityTypeBuilder<StockReservation> builder)
    {
        builder.HasKey(r => r.Id);

        builder.Property(r => r.Status)
            .HasConversion<int>();

        builder.HasIndex(r => new { r.OrderId, r.ProductId }).IsUnique();

        builder.HasIndex(r => r.OrderId);
    }
}
