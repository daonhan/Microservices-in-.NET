using Inventory.Service.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Inventory.Service.Infrastructure.Data.EntityFramework;

internal class StockItemConfiguration : IEntityTypeConfiguration<StockItem>
{
    public void Configure(EntityTypeBuilder<StockItem> builder)
    {
        builder.HasKey(s => s.ProductId);

        builder.Property(s => s.ProductId)
            .ValueGeneratedNever();

        builder.Property(s => s.RowVersion)
            .IsRowVersion();

        builder.Ignore(s => s.Available);
    }
}
