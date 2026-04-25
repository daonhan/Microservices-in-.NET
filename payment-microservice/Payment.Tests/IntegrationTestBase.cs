using ECommerce.Shared.Infrastructure.RabbitMq;
using Microsoft.Extensions.DependencyInjection;
using Payment.Service.Infrastructure.Data.EntityFramework;
using Payment.Tests.Authentication;

namespace Payment.Tests;

public class IntegrationTestBase : IClassFixture<PaymentWebApplicationFactory>
{
    internal readonly PaymentWebApplicationFactory Factory;
    internal readonly PaymentContext PaymentContext;
    internal readonly HttpClient HttpClient;
    internal readonly IRabbitMqConnection RabbitMqConnection;

    public IntegrationTestBase(PaymentWebApplicationFactory webApplicationFactory)
    {
        Factory = webApplicationFactory;

        var scope = webApplicationFactory.Services.CreateScope();
        PaymentContext = scope.ServiceProvider.GetRequiredService<PaymentContext>();
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
