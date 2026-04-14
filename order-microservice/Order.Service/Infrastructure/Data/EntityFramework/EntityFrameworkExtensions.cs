using Microsoft.EntityFrameworkCore;

namespace Order.Service.Infrastructure.Data.EntityFramework;

public static class EntityFrameworkExtensions
{
    public static void AddSqlServerDatastore(this IServiceCollection services,
        IConfigurationManager configuration)
    {
        services.AddDbContext<OrderContext>(options =>
            options.UseSqlServer(configuration.GetConnectionString("Default")));

        services.AddScoped<IOrderStore, OrderContext>();
    }

    public static void MigrateDatabase(this WebApplication webApp)
    {
        using var scope = webApp.Services.CreateScope();
        using var orderContext = scope.ServiceProvider.GetRequiredService<OrderContext>();
        orderContext.Database.Migrate();
    }
}
