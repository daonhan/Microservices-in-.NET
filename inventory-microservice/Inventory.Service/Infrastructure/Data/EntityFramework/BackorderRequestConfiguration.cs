using Inventory.Service.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Inventory.Service.Infrastructure.Data.EntityFramework;

internal class BackorderRequestConfiguration : IEntityTypeConfiguration<BackorderRequest>
{
    public void Configure(EntityTypeBuilder<BackorderRequest> builder)
    {
        builder.HasKey(b => b.Id);

        builder.Property(b => b.CustomerId)
            .IsRequired()
            .HasMaxLength(100);

        builder.HasIndex(b => new { b.ProductId, b.FulfilledAt, b.CreatedAt });
    }
}
