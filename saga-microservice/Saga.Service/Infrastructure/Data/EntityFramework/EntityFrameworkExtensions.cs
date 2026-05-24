using Microsoft.EntityFrameworkCore;
using Saga.Service.Domain.Abstractions;

namespace Saga.Service.Infrastructure.Data.EntityFramework;

public static class EntityFrameworkExtensions
{
    public static void AddSqlServerDatastore(this IServiceCollection services,
        IConfigurationManager configuration)
    {
        services.AddDbContext<SagaContext>(options =>
            options.UseSqlServer(configuration.GetConnectionString("Default"),
                sqlServerOptionsAction: sqlOptions =>
                {
                    sqlOptions.EnableRetryOnFailure(
                        maxRetryCount: 5,
                        maxRetryDelay: TimeSpan.FromSeconds(40),
                        errorNumbersToAdd: [0]);
                }));

        services.AddScoped<ISagaInstanceStore, EfSagaInstanceStore>();
    }

    public static void MigrateDatabase(this WebApplication webApp)
    {
        using var scope = webApp.Services.CreateScope();
        using var sagaContext = scope.ServiceProvider.GetRequiredService<SagaContext>();
        sagaContext.Database.Migrate();
    }
}
