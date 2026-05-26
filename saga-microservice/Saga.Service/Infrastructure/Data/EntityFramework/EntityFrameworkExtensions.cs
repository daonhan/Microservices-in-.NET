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

    public static void SeedQaOperatorOutboxFixture(this WebApplication webApp)
    {
        var commandId = new Guid("e0000000-0000-0000-0000-000000000003");
        using var scope = webApp.Services.CreateScope();
        using var sagaContext = scope.ServiceProvider.GetRequiredService<SagaContext>();
        sagaContext.Database.ExecuteSqlInterpolated(
            $@"IF NOT EXISTS (SELECT 1 FROM [OutboxEvents] WHERE [Id] = {commandId})
               INSERT INTO [OutboxEvents] ([Id], [EventType], [Data], [Sent], [Status], [Attempts])
               VALUES ({commandId}, N'Saga.Service.QaSyntheticEvent, Saga.Service', N'{{}}', 1, 0, 0);");
    }
}
