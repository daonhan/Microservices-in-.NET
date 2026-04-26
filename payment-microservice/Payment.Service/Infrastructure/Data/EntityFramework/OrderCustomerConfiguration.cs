using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Payment.Service.Models;

namespace Payment.Service.Infrastructure.Data.EntityFramework;

internal class OrderCustomerConfiguration : IEntityTypeConfiguration<OrderCustomer>
{
    public void Configure(EntityTypeBuilder<OrderCustomer> builder)
    {
        builder.HasKey(o => o.OrderId);

        builder.Property(o => o.CustomerId)
            .IsRequired()
            .HasMaxLength(100);
    }
}
