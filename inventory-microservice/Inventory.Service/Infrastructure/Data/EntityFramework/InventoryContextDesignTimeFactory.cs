using ECommerce.Shared.Observability.Metrics;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Inventory.Service.Infrastructure.Data.EntityFramework;

internal class InventoryContextDesignTimeFactory : IDesignTimeDbContextFactory<InventoryContext>
{
    public InventoryContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<InventoryContext>();
        optionsBuilder.UseSqlServer("Server=localhost,1433;Database=Inventory;User Id=sa;Password=micR0S3rvice$;TrustServerCertificate=True");

        return new InventoryContext(optionsBuilder.Options, new MetricFactory("Inventory"));
    }
}
