using ECommerce.Shared.Qa;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Order.Service.Domain;

namespace Order.Service.Infrastructure.Data.EntityFramework;

internal class OrderConfiguration : IEntityTypeConfiguration<Domain.Order>
{
    private static readonly DateTime SeedEpoch = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    public void Configure(EntityTypeBuilder<Domain.Order> builder)
    {
        builder.HasKey(o => o.OrderId);

        builder.Property(o => o.CustomerId)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(o => o.Status)
            .HasConversion<int>();

        builder.HasMany(o => o.OrderProducts)
            .WithOne()
            .HasForeignKey(op => op.OrderId);

        builder.Navigation(o => o.OrderProducts)
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasData(
            new
            {
                OrderId = QaPersonas.OrderAuthorizedId,
                CustomerId = QaPersonas.CustomerHappyId.ToString(),
                Status = OrderStatus.Confirmed,
                OrderDate = SeedEpoch,
            },
            new
            {
                OrderId = QaPersonas.OrderCapturedId,
                CustomerId = QaPersonas.CustomerHappyId.ToString(),
                Status = OrderStatus.Confirmed,
                OrderDate = SeedEpoch,
            });
    }
}
