using Inventory.Service.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Inventory.Service.Infrastructure.Data.EntityFramework;

internal class StockLevelConfiguration : IEntityTypeConfiguration<StockLevel>
{
    public void Configure(EntityTypeBuilder<StockLevel> builder)
    {
        builder.HasKey(l => l.Id);

        builder.HasIndex(l => new { l.ProductId, l.WarehouseId }).IsUnique();

        builder.Property(l => l.RowVersion)
            .IsRowVersion();

        builder.HasOne(l => l.Warehouse)
            .WithMany()
            .HasForeignKey(l => l.WarehouseId);
    }
}
