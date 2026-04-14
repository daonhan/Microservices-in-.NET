using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Order.Service.Infrastructure.Data.EntityFramework;

internal class OrderConfiguration : IEntityTypeConfiguration<Models.Order>
{
    public void Configure(EntityTypeBuilder<Models.Order> builder)
    {
        builder.HasKey(o => o.OrderId);

        builder.Property(o => o.CustomerId)
            .IsRequired()
            .HasMaxLength(100);

        builder.HasMany(o => o.OrderProducts)
            .WithOne()
            .HasForeignKey(op => op.OrderId);

        builder.Navigation(o => o.OrderProducts)
            .UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
