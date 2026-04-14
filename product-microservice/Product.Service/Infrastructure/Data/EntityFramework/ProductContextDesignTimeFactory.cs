using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Product.Service.Infrastructure.Data.EntityFramework;

internal class ProductContextDesignTimeFactory : IDesignTimeDbContextFactory<ProductContext>
{
    public ProductContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<ProductContext>();
        optionsBuilder.UseSqlServer("Server=localhost,1433;Database=Product;User Id=sa;Password=micR0S3rvice$;TrustServerCertificate=True");

        return new ProductContext(optionsBuilder.Options);
    }
}
