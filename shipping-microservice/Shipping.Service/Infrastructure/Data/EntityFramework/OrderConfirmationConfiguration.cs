using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Shipping.Service.Domain;

namespace Shipping.Service.Infrastructure.Data.EntityFramework;

internal class OrderConfirmationConfiguration : IEntityTypeConfiguration<OrderConfirmation>
{
    public void Configure(EntityTypeBuilder<OrderConfirmation> builder)
    {
        builder.HasKey(o => o.OrderId);

        builder.Property(o => o.CustomerId)
            .IsRequired()
            .HasMaxLength(100);
    }
}
