using System.Globalization;
using ECommerce.Shared.Qa;
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

        builder.HasData(
            new
            {
                Id = QaPersonas.OrderProductAuthorizedId,
                OrderId = QaPersonas.OrderAuthorizedId,
                ProductId = QaPersonas.ProductHappyId.ToString(CultureInfo.InvariantCulture),
                Quantity = QaPersonas.ProductHappyQuantity,
            },
            new
            {
                Id = QaPersonas.OrderProductCapturedId,
                OrderId = QaPersonas.OrderCapturedId,
                ProductId = QaPersonas.ProductHappyId.ToString(CultureInfo.InvariantCulture),
                Quantity = QaPersonas.ProductHappyQuantity,
            });
    }
}
