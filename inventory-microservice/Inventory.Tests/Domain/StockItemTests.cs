using Inventory.Service.Models;

namespace Inventory.Tests.Domain;

public class StockItemTests
{
    [Fact]
    public void Available_IsOnHandMinusReserved()
    {
        var item = new StockItem
        {
            ProductId = 1,
            TotalOnHand = 10,
            TotalReserved = 3,
        };

        Assert.Equal(7, item.Available);
    }

    [Fact]
    public void Restock_IncrementsOnHand_AndAvailableIncreasesBySameAmount()
    {
        var item = new StockItem
        {
            ProductId = 1,
            TotalOnHand = 5,
            TotalReserved = 2,
        };

        item.TotalOnHand += 4;

        Assert.Equal(9, item.TotalOnHand);
        Assert.Equal(7, item.Available);
    }

    [Fact]
    public void Restock_WhenReservedExceedsOnHand_AvailableCanBeNegative()
    {
        var item = new StockItem
        {
            ProductId = 1,
            TotalOnHand = 2,
            TotalReserved = 5,
        };

        Assert.Equal(-3, item.Available);
    }
}
