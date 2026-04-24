using ECommerce.Shared.Infrastructure.RabbitMq;
using Microsoft.Extensions.DependencyInjection;
using Shipping.Service.Infrastructure.Data.EntityFramework;
using Shipping.Tests.Authentication;

namespace Shipping.Tests;

public class IntegrationTestBase : IClassFixture<ShippingWebApplicationFactory>
{
    internal readonly ShippingWebApplicationFactory Factory;
    internal readonly ShippingContext ShippingContext;
    internal readonly HttpClient HttpClient;
    internal readonly IRabbitMqConnection RabbitMqConnection;

    public IntegrationTestBase(ShippingWebApplicationFactory webApplicationFactory)
    {
        Factory = webApplicationFactory;

        var scope = webApplicationFactory.Services.CreateScope();
        ShippingContext = scope.ServiceProvider.GetRequiredService<ShippingContext>();
        HttpClient = webApplicationFactory.CreateClient();
        RabbitMqConnection = scope.ServiceProvider.GetRequiredService<IRabbitMqConnection>();
    }

    protected HttpClient CreateAuthenticatedClient(string role = "Administrator")
    {
        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.RoleHeader, role);
        return client;
    }
}
