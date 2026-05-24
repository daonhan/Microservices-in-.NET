using System.Net;
using System.Net.Http.Json;
using Payment.Tests.Authentication;
using GetByOrderPaymentResponse = Payment.Service.Features.GetPaymentByOrder.PaymentResponse;

namespace Payment.Tests.Features.GetPaymentByOrder;

public class GetPaymentByOrderOwnershipTests : IntegrationTestBase
{
    public GetPaymentByOrderOwnershipTests(PaymentWebApplicationFactory webApplicationFactory)
        : base(webApplicationFactory)
    {
    }

    [Fact]
    public async Task GetByOrder_WhenCustomerOwnsPayment_ReturnsOk()
    {
        var (orderId, paymentId, customerId) = await SeedPaymentAsync();

        var response = await CreateCustomerClient(customerId).GetAsync($"/by-order/{orderId}");

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<GetByOrderPaymentResponse>();
        Assert.NotNull(body);
        Assert.Equal(paymentId, body.PaymentId);
        Assert.Equal(orderId, body.OrderId);
        Assert.Equal(customerId, body.CustomerId);
    }

    [Fact]
    public async Task GetByOrder_WhenCustomerIsNotOwner_ReturnsNotFound()
    {
        var (orderId, _, _) = await SeedPaymentAsync();

        var response = await CreateCustomerClient("different-customer").GetAsync($"/by-order/{orderId}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetByOrder_WhenAdmin_ReturnsOkRegardlessOfOwnership()
    {
        var (orderId, paymentId, _) = await SeedPaymentAsync();

        var response = await CreateAuthenticatedClient().GetAsync($"/by-order/{orderId}");

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<GetByOrderPaymentResponse>();
        Assert.NotNull(body);
        Assert.Equal(paymentId, body.PaymentId);
    }

    [Fact]
    public async Task GetByOrder_WhenUnauthenticated_ReturnsUnauthorized()
    {
        var response = await HttpClient.GetAsync($"/by-order/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetByOrder_WhenNotFound_ReturnsNotFound()
    {
        var response = await CreateAuthenticatedClient().GetAsync($"/by-order/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private async Task<(Guid OrderId, Guid PaymentId, string CustomerId)> SeedPaymentAsync()
    {
        var orderId = Guid.NewGuid();
        var paymentId = Guid.NewGuid();
        var customerId = $"cust-{Guid.NewGuid():N}";
        var payment = Service.Domain.Payment.Create(
            paymentId: paymentId,
            orderId: orderId,
            customerId: customerId,
            amount: 50.00m,
            currency: "USD",
            createdAt: DateTime.UtcNow);
        PaymentContext.Payments.Add(payment);
        await PaymentContext.SaveChangesAsync();
        return (orderId, paymentId, customerId);
    }

    private HttpClient CreateCustomerClient(string customerId)
    {
        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.RoleHeader, "Customer");
        client.DefaultRequestHeaders.Add(TestAuthHandler.CustomerIdHeader, customerId);
        return client;
    }
}
