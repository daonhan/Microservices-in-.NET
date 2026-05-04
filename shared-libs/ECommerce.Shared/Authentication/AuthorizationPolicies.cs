using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;

namespace ECommerce.Shared.Authentication;

public static class AuthorizationPolicies
{
    public const string RoleClaimType = "user_role";
    public const string OperatorRole = "Operator";
    public const string RequireOperatorPolicy = "RequireOperator";

    public static AuthorizationBuilder AddRequireOperator(this AuthorizationBuilder builder) =>
        builder.AddPolicy(RequireOperatorPolicy, policy => policy
            .RequireAuthenticatedUser()
            .RequireClaim(RoleClaimType, OperatorRole));

    public static IServiceCollection AddRequireOperatorPolicy(this IServiceCollection services) =>
        services.AddAuthorizationBuilder()
            .AddRequireOperator()
            .Services;
}
