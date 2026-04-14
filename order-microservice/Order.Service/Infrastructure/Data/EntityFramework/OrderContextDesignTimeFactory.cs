using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Order.Service.Infrastructure.Data.EntityFramework;

internal class OrderContextDesignTimeFactory : IDesignTimeDbContextFactory<OrderContext>
{
    public OrderContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<OrderContext>();
        optionsBuilder.UseSqlServer("Server=localhost,1433;Database=Order;User Id=sa;Password=micR0S3rvice$;TrustServerCertificate=True");

        return new OrderContext(optionsBuilder.Options);
    }
}
