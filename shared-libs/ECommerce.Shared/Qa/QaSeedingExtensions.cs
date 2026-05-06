using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace ECommerce.Shared.Qa;

public static class QaSeedingExtensions
{
    public static bool IsQaSeedingEnabled(IHostEnvironment environment, IConfiguration configuration)
        => environment.IsDevelopment() || configuration.GetValue("Qa:Seed", defaultValue: false);

    public static IServiceCollection AddQaSeeding<TSeeder>(this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
        where TSeeder : class, IHostedService
    {
        if (IsQaSeedingEnabled(environment, configuration))
        {
            services.AddHostedService<TSeeder>();
        }

        return services;
    }

    public static void SeedQaData(this WebApplication app, Action<IServiceProvider>? seedAction = null)
    {
        if (!IsQaSeedingEnabled(app.Environment, app.Configuration))
        {
            return;
        }

        if (seedAction is null)
        {
            return;
        }

        using var scope = app.Services.CreateScope();
        seedAction(scope.ServiceProvider);
    }
}
