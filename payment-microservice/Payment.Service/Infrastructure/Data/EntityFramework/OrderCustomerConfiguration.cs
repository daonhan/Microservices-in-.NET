using ECommerce.Shared.Qa;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Payment.Service.Domain;

namespace Payment.Service.Infrastructure.Data.EntityFramework;

internal class OrderCustomerConfiguration : IEntityTypeConfiguration<OrderCustomer>
{
    public void Configure(EntityTypeBuilder<OrderCustomer> builder)
    {
        builder.HasKey(o => o.OrderId);

        builder.Property(o => o.CustomerId)
            .IsRequired()
            .HasMaxLength(100);

        builder.HasData(
            new OrderCustomer
            {
                OrderId = QaPersonas.OrderAuthorizedId,
                CustomerId = QaPersonas.CustomerHappyId.ToString(),
                ReceivedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            },
            new OrderCustomer
            {
                OrderId = QaPersonas.OrderCapturedId,
                CustomerId = QaPersonas.CustomerHappyId.ToString(),
                ReceivedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            });
    }
}
