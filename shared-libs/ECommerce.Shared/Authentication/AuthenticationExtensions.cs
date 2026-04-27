using System.Diagnostics.Metrics;
using System.IdentityModel.Tokens.Jwt;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;

namespace ECommerce.Shared.Authentication;

public static partial class AuthenticationExtensions
{
    /// <summary>
    /// Legacy symmetric key — kept for the dual-validator window (1.19.0).
    /// Will be removed in 2.0.0 once all consumers confirm zero HS256 validations.
    /// </summary>
    public const string SecurityKey = "kR^86SSZu&10RQ1%^k84hii1poPW^CG*";

    private static readonly Meter JwtMeter = new("ECommerce.Shared.Jwt");
    private static readonly Counter<long> ValidationFailureCounter =
        JwtMeter.CreateCounter<long>("jwt-validation-failure", description: "JWT validation failures by category");
    private static readonly Counter<long> ValidationSuccessCounter =
        JwtMeter.CreateCounter<long>("jwt-validation-success", description: "JWT validation successes by algorithm");

    /// <summary>
    /// Registers JWT bearer authentication that accepts both HS256 (legacy) and RS256 (JWKS).
    /// During the dual-validator window tokens without a <c>kid</c> header are validated
    /// against the legacy symmetric key; tokens with a <c>kid</c> are validated via the
    /// JWKS endpoint published by Auth at <c>/.well-known/jwks.json</c>.
    /// </summary>
    public static void AddJwtAuthentication(this IServiceCollection services,
        IConfigurationManager configuration,
        IHostEnvironment? environment = null)
    {
        var authOptions = new AuthOptions();
        configuration.GetSection(AuthOptions.AuthenticationSectionName).Bind(authOptions);
        services.AddSingleton(authOptions);

        var isDevelopment = environment?.IsDevelopment() ?? true;

        var legacySymmetricKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(SecurityKey));

        services.AddAuthentication(opt =>
        {
            opt.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
            opt.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
        })
        .AddJwtBearer(options =>
        {
            // Wire Authority so the framework fetches /.well-known/jwks.json from Auth
            options.Authority = authOptions.AuthMicroserviceBaseAddress;
            options.RequireHttpsMetadata = !isDevelopment;

            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuer = authOptions.AuthMicroserviceBaseAddress,
                ValidateAudience = false,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                RequireSignedTokens = true,
                RequireExpirationTime = true,
                ClockSkew = TimeSpan.FromSeconds(30),
                // Accept both HS256 and RS256 during the dual-validator window
                ValidAlgorithms = new[] { SecurityAlgorithms.HmacSha256, SecurityAlgorithms.RsaSha256 },
                // Legacy HS256 key — also used when no kid is present
                IssuerSigningKey = legacySymmetricKey,
                // RS256 keys are resolved automatically via Authority/JWKS
            };

            options.Events = new JwtBearerEvents
            {
                OnTokenValidated = context =>
                {
                    var token = context.SecurityToken as JwtSecurityToken;
                    var algorithm = token?.Header?.Alg ?? "unknown";
                    ValidationSuccessCounter.Add(1,
                        new KeyValuePair<string, object?>("algorithm", algorithm));
                    return Task.CompletedTask;
                },
                OnAuthenticationFailed = context =>
                {
                    var logger = context.HttpContext.RequestServices
                        .GetRequiredService<ILoggerFactory>()
                        .CreateLogger("ECommerce.Shared.Authentication");

                    var category = CategorizeFailure(context.Exception);
                    var kid = ExtractKid(context);

                    LogValidationFailed(logger, category, kid ?? "(none)");

                    ValidationFailureCounter.Add(1,
                        new KeyValuePair<string, object?>("category", category));

                    return Task.CompletedTask;
                }
            };
        });

        services.AddAuthorization();
    }

    public static void UseJwtAuthentication(this WebApplication app) =>
        app.UseAuthentication().UseAuthorization();

    private static string CategorizeFailure(Exception exception)
    {
        return exception switch
        {
            SecurityTokenExpiredException => "expired",
            SecurityTokenInvalidSignatureException => "bad-signature",
            SecurityTokenInvalidIssuerException => "bad-issuer",
            SecurityTokenInvalidAlgorithmException => "algorithm-rejected",
            SecurityTokenNotYetValidException => "not-yet-valid",
            _ => "other"
        };
    }

    private static string? ExtractKid(AuthenticationFailedContext context)
    {
        try
        {
            // Try to read the kid from the failing token's header
            var authHeader = context.HttpContext.Request.Headers.Authorization.FirstOrDefault();
            if (authHeader is null || !authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            var tokenString = authHeader["Bearer ".Length..].Trim();
            var handler = new JwtSecurityTokenHandler();
            if (handler.CanReadToken(tokenString))
            {
                var jwt = handler.ReadJwtToken(tokenString);
                return jwt.Header.Kid;
            }
        }
        catch
        {
            // Never let kid extraction bubble up
        }

        return null;
    }

    [LoggerMessage(
        EventId = 1,
        Level = LogLevel.Warning,
        Message = "JWT validation failed: category={Category}, kid={Kid}")]
    private static partial void LogValidationFailed(ILogger logger, string category, string kid);
}
