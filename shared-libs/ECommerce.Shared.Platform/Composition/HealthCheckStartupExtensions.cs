using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace ECommerce.Shared.HealthChecks;

public static class HealthCheckStartupExtensions
{
    public static IHealthChecksBuilder AddPlatformHealthChecks(this IServiceCollection services) =>
        services.AddHealthChecks();

    public static IHealthChecksBuilder AddSqlServerProbe(
        this IHealthChecksBuilder builder, string connectionString) =>
        builder.AddCheck("sqlserver", new SqlServerHealthCheck(connectionString), tags: ["ready"]);

    public static IHealthChecksBuilder AddRabbitMqProbe(
        this IHealthChecksBuilder builder, string hostName) =>
        builder.AddCheck("rabbitmq", new RabbitMqHealthCheck(hostName), tags: ["ready"]);

    public static IHealthChecksBuilder AddRedisProbe(
        this IHealthChecksBuilder builder, string configuration) =>
        builder.AddCheck("redis", new RedisHealthCheck(configuration), tags: ["ready"]);

    public static void MapPlatformHealthChecks(this WebApplication app)
    {
        app.MapHealthChecks("/health/live", new HealthCheckOptions
        {
            Predicate = _ => false
        });

        app.MapHealthChecks("/health/ready", new HealthCheckOptions
        {
            Predicate = hc => hc.Tags.Contains("ready")
        });
    }
}
