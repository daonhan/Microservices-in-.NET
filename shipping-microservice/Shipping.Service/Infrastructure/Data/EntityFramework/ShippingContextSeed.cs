using Microsoft.EntityFrameworkCore;

namespace Shipping.Service.Infrastructure.Data.EntityFramework;

internal static class ShippingContextSeed
{
    public static void MigrateDatabase(this WebApplication webApp)
    {
        using var scope = webApp.Services.CreateScope();
        using var shippingContext = scope.ServiceProvider.GetRequiredService<ShippingContext>();
        shippingContext.Database.Migrate();
    }
}
