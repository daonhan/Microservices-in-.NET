using ECommerce.Shared.Qa;
using Inventory.Service.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Inventory.Service.Infrastructure.Data.EntityFramework;

internal class StockItemConfiguration : IEntityTypeConfiguration<StockItem>
{
    public void Configure(EntityTypeBuilder<StockItem> builder)
    {
        builder.HasKey(s => s.ProductId);

        builder.Property(s => s.ProductId)
            .ValueGeneratedNever();

        builder.Property(s => s.RowVersion)
            .IsRowVersion();

        builder.Ignore(s => s.Available);

        builder.HasData(
            new StockItem
            {
                ProductId = QaPersonas.ProductHappyId,
                TotalOnHand = QaPersonas.HappyPathStockOnHand,
                TotalReserved = 0,
                LowStockThreshold = QaPersonas.LowStockThreshold,
                RowVersion = [1]
            },
            new StockItem
            {
                ProductId = QaPersonas.ProductDeclineId,
                TotalOnHand = QaPersonas.DeclinePathStockOnHand,
                TotalReserved = 0,
                LowStockThreshold = QaPersonas.LowStockThreshold,
                RowVersion = [1]
            },
            new StockItem
            {
                ProductId = QaPersonas.ProductZeroStockId,
                TotalOnHand = QaPersonas.ZeroStockOnHand,
                TotalReserved = 0,
                LowStockThreshold = QaPersonas.LowStockThreshold,
                RowVersion = [1]
            },
            new StockItem
            {
                ProductId = QaPersonas.ProductLowStockId,
                TotalOnHand = QaPersonas.LowStockOnHand,
                TotalReserved = 0,
                LowStockThreshold = QaPersonas.LowStockTrippedThreshold,
                RowVersion = [1]
            },
            new StockItem
            {
                ProductId = QaPersonas.ProductRestockTargetId,
                TotalOnHand = QaPersonas.RestockTargetOnHand,
                TotalReserved = 0,
                LowStockThreshold = QaPersonas.LowStockThreshold,
                RowVersion = [1]
            });
    }
}
