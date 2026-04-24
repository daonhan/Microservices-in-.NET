using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Shipping.Service.Infrastructure.Data.EntityFramework;

internal class ShippingContextDesignTimeFactory : IDesignTimeDbContextFactory<ShippingContext>
{
    public ShippingContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<ShippingContext>();
        optionsBuilder.UseSqlServer("Server=localhost,1433;Database=Shipping;User Id=sa;Password=micR0S3rvice$;TrustServerCertificate=True");

        return new ShippingContext(optionsBuilder.Options);
    }
}
