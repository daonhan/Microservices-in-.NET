using ECommerce.Shared.Qa;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Product.Service.Infrastructure.Data.EntityFramework;

internal class ProductConfiguration : IEntityTypeConfiguration<Domain.Product>
{
    public void Configure(EntityTypeBuilder<Domain.Product> builder)
    {
        builder.HasKey(p => p.Id);

        builder.Property(p => p.Name)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(p => p.Price)
            .IsRequired()
            .HasColumnType("decimal(18,4)");

        builder.HasOne(p => p.ProductType)
            .WithMany();

        builder.HasData(
            new Domain.Product
            {
                Id = QaPersonas.ProductHappyId,
                Name = QaPersonas.ProductHappyName,
                Description = "QA happy-path product",
                Price = QaPersonas.ProductHappyPrice,
                ProductTypeId = 1
            },
            new Domain.Product
            {
                Id = QaPersonas.ProductDeclineId,
                Name = QaPersonas.ProductDeclineName,
                Description = "QA payment-decline product (cents == 99)",
                Price = QaPersonas.ProductDeclinePrice,
                ProductTypeId = 1
            },
            new Domain.Product
            {
                Id = QaPersonas.ProductZeroStockId,
                Name = QaPersonas.ProductZeroStockName,
                Description = "QA stock-shortage product (zero on hand)",
                Price = QaPersonas.ProductZeroStockPrice,
                ProductTypeId = 1
            },
            new Domain.Product
            {
                Id = QaPersonas.ProductLowStockId,
                Name = QaPersonas.ProductLowStockName,
                Description = "QA inventory admin-ops product (threshold tripped)",
                Price = QaPersonas.ProductLowStockPrice,
                ProductTypeId = 1
            },
            new Domain.Product
            {
                Id = QaPersonas.ProductRestockTargetId,
                Name = QaPersonas.ProductRestockTargetName,
                Description = "QA inventory admin-ops product (restock target)",
                Price = QaPersonas.ProductRestockTargetPrice,
                ProductTypeId = 1
            });
    }
}
