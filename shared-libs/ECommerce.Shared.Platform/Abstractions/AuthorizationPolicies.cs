using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;

namespace ECommerce.Shared.Authentication;

public static class AuthorizationPolicies
{
    public const string RoleClaimType = "user_role";
    public const string OperatorRole = "Operator";
    public const string ServiceRole = "service";
    public const string RequireOperatorPolicy = "RequireOperator";
    public const string RequireServicePolicy = "RequireService";

    public static AuthorizationBuilder AddRequireOperator(this AuthorizationBuilder builder) =>
        builder.AddPolicy(RequireOperatorPolicy, policy => policy
            .RequireAuthenticatedUser()
            .RequireClaim(RoleClaimType, OperatorRole));

    public static AuthorizationBuilder AddRequireService(this AuthorizationBuilder builder) =>
        builder.AddPolicy(RequireServicePolicy, policy => policy
            .RequireAuthenticatedUser()
            .RequireClaim(RoleClaimType, ServiceRole));

    public static IServiceCollection AddRequireOperatorPolicy(this IServiceCollection services) =>
        services.AddAuthorizationBuilder()
            .AddRequireOperator()
            .Services;

    public static IServiceCollection AddRequireServicePolicy(this IServiceCollection services) =>
        services.AddAuthorizationBuilder()
            .AddRequireService()
            .Services;
}
