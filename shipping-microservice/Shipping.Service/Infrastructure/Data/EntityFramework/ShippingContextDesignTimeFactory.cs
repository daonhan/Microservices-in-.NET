using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Shipping.Service.Infrastructure.Data.EntityFramework;

internal class ShippingContextDesignTimeFactory : IDesignTimeDbContextFactory<ShippingContext>
{
    public ShippingContext CreateDbContext(string[] args)
    {
        var environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Development";
        var configuration = new Microsoft.Extensions.Configuration.ConfigurationBuilder()
            .SetBasePath(System.IO.Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
            .AddJsonFile($"appsettings.{environment}.json", optional: true)
            .AddEnvironmentVariables()
            .Build();

        var optionsBuilder = new DbContextOptionsBuilder<ShippingContext>();
        optionsBuilder.UseSqlServer(configuration.GetConnectionString("Default"));

        return new ShippingContext(optionsBuilder.Options);
    }
}
