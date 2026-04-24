using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Shipping.Service.Models;

namespace Shipping.Service.Infrastructure.Data.EntityFramework;

internal class WarehouseConfiguration : IEntityTypeConfiguration<Warehouse>
{
    public void Configure(EntityTypeBuilder<Warehouse> builder)
    {
        builder.HasKey(w => w.Id);

        builder.Property(w => w.Code)
            .IsRequired()
            .HasMaxLength(50);

        builder.Property(w => w.Name)
            .IsRequired()
            .HasMaxLength(100);

        builder.HasIndex(w => w.Code).IsUnique();

        builder.HasData(
            new Warehouse { Id = 1, Code = "WH-EAST", Name = "East Coast Warehouse" },
            new Warehouse { Id = 2, Code = "WH-WEST", Name = "West Coast Warehouse" });
    }
}
