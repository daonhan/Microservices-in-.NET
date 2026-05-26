using ECommerce.Shared.Infrastructure.Messaging;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

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
        var provider = MessagingProviderResolver.Resolve(configuration);
        services.AddProviderDeadLetterServices(provider);
        services.AddScoped<IDeadLetterReplayer>(sp => new DeadLetterReplayer(
            sp.GetRequiredService<IDeadLetterStore>(),
            sp.GetRequiredService<IDeadLetterPublisher>(),
            sp.GetRequiredService<ILogger<DeadLetterReplayer>>(),
            provider));
        services.AddScoped<IDeadLetterDiscarder>(sp => new DeadLetterDiscarder(
            sp.GetRequiredService<IDeadLetterStore>(),
            sp.GetRequiredService<ILogger<DeadLetterDiscarder>>(),
            provider));

        return services;
    }

    private static IServiceCollection AddProviderDeadLetterServices(this IServiceCollection services, string provider)
    {
        switch (provider)
        {
            case MessagingOptions.RabbitMqProvider:
                services.AddSingleton<IDeadLetterPublisher, RabbitMqDeadLetterPublisher>();
                services.AddDeadLetterCapture<RabbitMqDeadLetterCapture>();
                break;
            case MessagingOptions.AzureServiceBusProvider:
                services.AddSingleton<IDeadLetterPublisher, AzureServiceBusDeadLetterPublisher>();
                services.AddDeadLetterCapture<AzureServiceBusDeadLetterCapture>();
                break;
            default:
                throw new InvalidOperationException($"Unhandled messaging provider '{provider}'.");
        }

        return services;
    }

    private static IServiceCollection AddDeadLetterCapture<TCapture>(this IServiceCollection services)
        where TCapture : class, IDeadLetterCapture
    {
        services.AddSingleton<IDeadLetterCapture, TCapture>();
        services.AddSingleton<IHostedService>(sp => sp.GetRequiredService<IDeadLetterCapture>());
        return services;
    }

    public static void ApplyDeadLetterMigrations(this WebApplication app)
    {
        using var scope = app.Services.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<DeadLetterDbContext>();
        ctx.Database.Migrate();
    }
}
