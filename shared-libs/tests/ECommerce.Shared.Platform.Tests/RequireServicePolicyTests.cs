using System.Security.Claims;
using ECommerce.Shared.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;

namespace ECommerce.Shared.Tests;

public sealed class RequireServicePolicyTests
{
    [Fact]
    public async Task Given_principal_with_service_role_When_evaluating_RequireService_Then_succeeds()
    {
        var authorize = BuildAuthorizationService();
        var principal = BuildPrincipal(AuthorizationPolicies.ServiceRole);

        var result = await authorize.AuthorizeAsync(principal, AuthorizationPolicies.RequireServicePolicy);

        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task Given_principal_with_user_role_When_evaluating_RequireService_Then_fails()
    {
        var authorize = BuildAuthorizationService();
        var principal = BuildPrincipal("user");

        var result = await authorize.AuthorizeAsync(principal, AuthorizationPolicies.RequireServicePolicy);

        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task Given_unauthenticated_principal_When_evaluating_RequireService_Then_fails()
    {
        var authorize = BuildAuthorizationService();
        var principal = new ClaimsPrincipal(new ClaimsIdentity());

        var result = await authorize.AuthorizeAsync(principal, AuthorizationPolicies.RequireServicePolicy);

        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task Given_existing_RequireOperator_registration_When_adding_RequireService_Then_both_policies_resolve()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddRequireOperatorPolicy();
        services.AddRequireServicePolicy();
        var provider = services.BuildServiceProvider();
        var authorize = provider.GetRequiredService<IAuthorizationService>();

        var operatorPrincipal = BuildPrincipal(AuthorizationPolicies.OperatorRole);
        var servicePrincipal = BuildPrincipal(AuthorizationPolicies.ServiceRole);

        var operatorResult = await authorize.AuthorizeAsync(operatorPrincipal, AuthorizationPolicies.RequireOperatorPolicy);
        var serviceResult = await authorize.AuthorizeAsync(servicePrincipal, AuthorizationPolicies.RequireServicePolicy);

        Assert.True(operatorResult.Succeeded);
        Assert.True(serviceResult.Succeeded);
    }

    [Fact]
    public async Task Given_AddRequireServicePolicy_called_twice_When_evaluating_Then_policy_still_works()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddRequireServicePolicy();
        services.AddRequireServicePolicy();
        var provider = services.BuildServiceProvider();
        var authorize = provider.GetRequiredService<IAuthorizationService>();

        var principal = BuildPrincipal(AuthorizationPolicies.ServiceRole);

        var result = await authorize.AuthorizeAsync(principal, AuthorizationPolicies.RequireServicePolicy);

        Assert.True(result.Succeeded);
    }

    private static IAuthorizationService BuildAuthorizationService()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddRequireServicePolicy();
        return services.BuildServiceProvider().GetRequiredService<IAuthorizationService>();
    }

    private static ClaimsPrincipal BuildPrincipal(string role)
    {
        var identity = new ClaimsIdentity(
            new[] { new Claim(AuthorizationPolicies.RoleClaimType, role) },
            authenticationType: "Test");
        return new ClaimsPrincipal(identity);
    }
}
