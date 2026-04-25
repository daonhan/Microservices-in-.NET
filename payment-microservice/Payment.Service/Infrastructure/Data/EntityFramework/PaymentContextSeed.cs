using Microsoft.EntityFrameworkCore;

namespace Payment.Service.Infrastructure.Data.EntityFramework;

internal static class PaymentContextSeed
{
    public static void MigrateDatabase(this WebApplication webApp)
    {
        using var scope = webApp.Services.CreateScope();
        using var paymentContext = scope.ServiceProvider.GetRequiredService<PaymentContext>();
        paymentContext.Database.Migrate();
    }
}
