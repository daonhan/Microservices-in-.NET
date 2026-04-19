using Inventory.Service.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Inventory.Service.Infrastructure.Data.EntityFramework;

internal class StockMovementConfiguration : IEntityTypeConfiguration<StockMovement>
{
    public void Configure(EntityTypeBuilder<StockMovement> builder)
    {
        builder.HasKey(m => m.Id);

        builder.Property(m => m.Type)
            .HasConversion<int>();

        builder.Property(m => m.Reason)
            .HasMaxLength(200);

        builder.HasIndex(m => new { m.ProductId, m.OccurredAt });
    }
}
