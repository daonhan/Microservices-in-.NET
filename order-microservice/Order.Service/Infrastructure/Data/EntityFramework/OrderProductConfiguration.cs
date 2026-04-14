using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Order.Service.Infrastructure.Data.EntityFramework;

internal class OrderProductConfiguration : IEntityTypeConfiguration<Models.OrderProduct>
{
    public void Configure(EntityTypeBuilder<Models.OrderProduct> builder)
    {
        builder.HasKey(op => op.Id);

        builder.Property(op => op.ProductId)
            .IsRequired()
            .HasMaxLength(100);
    }
}
