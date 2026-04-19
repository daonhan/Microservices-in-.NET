using Microsoft.EntityFrameworkCore;

namespace Inventory.Service.Infrastructure.Data.EntityFramework;

internal static class InventoryContextSeed
{
    public static void MigrateDatabase(this WebApplication webApp)
    {
        using var scope = webApp.Services.CreateScope();
        using var inventoryContext = scope.ServiceProvider.GetRequiredService<InventoryContext>();
        inventoryContext.Database.Migrate();
    }
}
