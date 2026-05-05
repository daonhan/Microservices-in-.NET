using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ECommerce.Shared.Infrastructure.DeadLetter;

public static class DeadLetterStartupExtensions
{
    public static IServiceCollection AddDeadLetter(this IServiceCollection services, IConfigurationManager configuration)
    {
        var connectionString = configuration.GetConnectionString("DeadLetter")
            ?? configuration.GetConnectionString("Default");

        services.AddDbContext<DeadLetterDbContext>(options =>
            options.UseSqlServer(connectionString,
                sql => sql.EnableRetryOnFailure(
                    maxRetryCount: 5,
                    maxRetryDelay: TimeSpan.FromSeconds(40),
                    errorNumbersToAdd: [0])));

        services.AddScoped<IDeadLetterStore>(sp => sp.GetRequiredService<DeadLetterDbContext>());
        services.AddSingleton<IDeadLetterPublisher, RabbitMqDeadLetterPublisher>();
        services.AddScoped<IDeadLetterReplayer, DeadLetterReplayer>();
        services.AddScoped<IDeadLetterDiscarder, DeadLetterDiscarder>();
        services.AddHostedService<DeadLetterHostedService>();

        return services;
    }

    public static void ApplyDeadLetterMigrations(this WebApplication app)
    {
        using var scope = app.Services.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<DeadLetterDbContext>();
        ctx.Database.Migrate();
    }
}
