using System.Reflection;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.OpenApi.Models;

namespace ECommerce.Shared.OpenApi;

public static class OpenApiStartupExtensions
{
    public const string DocumentName = "v1";
    public const string JwtSecurityScheme = "Bearer";

    public static IHostApplicationBuilder AddPlatformOpenApi(
        this IHostApplicationBuilder hostBuilder,
        string? serviceTag = null)
    {
        hostBuilder.Services.AddPlatformOpenApi(serviceTag);
        return hostBuilder;
    }

    public static IServiceCollection AddPlatformOpenApi(
        this IServiceCollection services,
        string? serviceTag = null)
    {
        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen(options =>
        {
            var entryAssembly = Assembly.GetEntryAssembly();
            var title = serviceTag ?? entryAssembly?.GetName().Name ?? "Service";

            options.SwaggerDoc(DocumentName, new OpenApiInfo
            {
                Title = title,
                Version = DocumentName
            });

            options.AddSecurityDefinition(JwtSecurityScheme, new OpenApiSecurityScheme
            {
                Name = "Authorization",
                Description = "JWT Bearer token issued by POST /login. Format: 'Bearer {token}'.",
                In = ParameterLocation.Header,
                Type = SecuritySchemeType.Http,
                Scheme = "bearer",
                BearerFormat = "JWT"
            });

            options.AddSecurityRequirement(new OpenApiSecurityRequirement
            {
                [new OpenApiSecurityScheme
                {
                    Reference = new OpenApiReference
                    {
                        Type = ReferenceType.SecurityScheme,
                        Id = JwtSecurityScheme
                    }
                }] = Array.Empty<string>()
            });

            if (entryAssembly is not null)
            {
                var xmlFile = Path.Combine(
                    AppContext.BaseDirectory,
                    $"{entryAssembly.GetName().Name}.xml");
                if (File.Exists(xmlFile))
                {
                    options.IncludeXmlComments(xmlFile);
                }
            }
        });

        return services;
    }

    public static WebApplication UsePlatformOpenApi(this WebApplication app)
    {
        if (app.Environment.IsProduction())
        {
            return app;
        }

        app.UseSwagger();
        return app;
    }
}
