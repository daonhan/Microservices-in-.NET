using System.Net;
using System.Net.Http.Json;
using ECommerce.Shared.Infrastructure.Outbox;
using Microsoft.Extensions.DependencyInjection;
using Payment.Service.Endpoints;
using Payment.Service.IntegrationEvents.Events;
using Payment.Service.Models;
using Payment.Tests.Authentication;

namespace Payment.Tests.Api;

public class PaymentEndpointsTests : IntegrationTestBase
{
    public PaymentEndpointsTests(PaymentWebApplicationFactory webApplicationFactory)
        : base(webApplicationFactory)
    {
    }

    [Fact]
    public async Task Capture_WhenAdminAndAuthorized_TransitionsToCaptured()
    {
        var paymentId = await SeedPaymentAsync(PaymentStatus.Authorized);

        var response = await CreateAuthenticatedClient().PostAsync(
            $"/{paymentId}/capture",
            content: null);

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<PaymentApiEndpoints.PaymentResponse>();
        Assert.NotNull(body);
        Assert.Equal(PaymentStatus.Captured.ToString(), body.Status);

        await AssertOutboxContainsAsync(nameof(PaymentCapturedEvent), paymentId, expectedCount: 1);
    }

    [Fact]
    public async Task Capture_WhenAlreadyCaptured_IsIdempotent()
    {
        var paymentId = await SeedPaymentAsync(PaymentStatus.Captured);

        var response = await CreateAuthenticatedClient().PostAsync(
            $"/{paymentId}/capture",
            content: null);

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<PaymentApiEndpoints.PaymentResponse>();
        Assert.NotNull(body);
        Assert.Equal(PaymentStatus.Captured.ToString(), body.Status);

        await AssertOutboxContainsAsync(nameof(PaymentCapturedEvent), paymentId, expectedCount: 0);
    }

    [Fact]
    public async Task Capture_WhenStatusNotAuthorized_ReturnsConflict()
    {
        var paymentId = await SeedPaymentAsync(PaymentStatus.Failed);

        var response = await CreateAuthenticatedClient().PostAsync(
            $"/{paymentId}/capture",
            content: null);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Capture_WhenNotFound_ReturnsNotFound()
    {
        var response = await CreateAuthenticatedClient().PostAsync(
            $"/{Guid.NewGuid()}/capture",
            content: null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Capture_WhenCallerIsCustomer_ReturnsForbidden()
    {
        var paymentId = await SeedPaymentAsync(PaymentStatus.Authorized);

        var response = await CreateCustomerClient("cust-1").PostAsync(
            $"/{paymentId}/capture",
            content: null);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Capture_WhenUnauthenticated_ReturnsUnauthorized()
    {
        var response = await HttpClient.PostAsync(
            $"/{Guid.NewGuid()}/capture",
            content: null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Refund_WhenAdminAndCaptured_TransitionsToRefunded()
    {
        var paymentId = await SeedPaymentAsync(PaymentStatus.Captured);

        var response = await CreateAuthenticatedClient().PostAsJsonAsync(
            $"/{paymentId}/refund",
            new PaymentApiEndpoints.RefundPaymentRequest(Amount: null));

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<PaymentApiEndpoints.PaymentResponse>();
        Assert.NotNull(body);
        Assert.Equal(PaymentStatus.Refunded.ToString(), body.Status);

        await AssertOutboxContainsAsync(nameof(PaymentRefundedEvent), paymentId, expectedCount: 1);
    }

    [Fact]
    public async Task Refund_WithEmptyBody_DefaultsToFullAmount()
    {
        var paymentId = await SeedPaymentAsync(PaymentStatus.Captured);

        var response = await CreateAuthenticatedClient().PostAsync(
            $"/{paymentId}/refund",
            content: null);

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<PaymentApiEndpoints.PaymentResponse>();
        Assert.NotNull(body);
        Assert.Equal(PaymentStatus.Refunded.ToString(), body.Status);
    }

    [Fact]
    public async Task Refund_WhenStatusNotCaptured_ReturnsConflict()
    {
        var paymentId = await SeedPaymentAsync(PaymentStatus.Authorized);

        var response = await CreateAuthenticatedClient().PostAsJsonAsync(
            $"/{paymentId}/refund",
            new PaymentApiEndpoints.RefundPaymentRequest(Amount: null));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Refund_WhenAlreadyRefunded_ReturnsConflict()
    {
        var paymentId = await SeedPaymentAsync(PaymentStatus.Refunded);

        var response = await CreateAuthenticatedClient().PostAsJsonAsync(
            $"/{paymentId}/refund",
            new PaymentApiEndpoints.RefundPaymentRequest(Amount: null));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Refund_WhenNotFound_ReturnsNotFound()
    {
        var response = await CreateAuthenticatedClient().PostAsJsonAsync(
            $"/{Guid.NewGuid()}/refund",
            new PaymentApiEndpoints.RefundPaymentRequest(Amount: null));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Refund_WhenCallerIsCustomer_ReturnsForbidden()
    {
        var paymentId = await SeedPaymentAsync(PaymentStatus.Captured);

        var response = await CreateCustomerClient("cust-1").PostAsJsonAsync(
            $"/{paymentId}/refund",
            new PaymentApiEndpoints.RefundPaymentRequest(Amount: null));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Refund_WhenUnauthenticated_ReturnsUnauthorized()
    {
        var response = await HttpClient.PostAsJsonAsync(
            $"/{Guid.NewGuid()}/refund",
            new PaymentApiEndpoints.RefundPaymentRequest(Amount: null));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private async Task<Guid> SeedPaymentAsync(PaymentStatus status)
    {
        var paymentId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        var payment = Service.Models.Payment.Create(
            paymentId: paymentId,
            orderId: orderId,
            customerId: $"cust-{Guid.NewGuid():N}",
            amount: 75.00m,
            currency: "USD",
            createdAt: now);

        if (status == PaymentStatus.Failed)
        {
            payment.Fail(now);
        }
        else if (status != PaymentStatus.Pending)
        {
            payment.Authorize($"INMEM-{Guid.NewGuid():N}", now);

            if (status == PaymentStatus.Captured || status == PaymentStatus.Refunded)
            {
                payment.Capture(now);
            }

            if (status == PaymentStatus.Refunded)
            {
                payment.Refund(now);
            }
        }

        PaymentContext.Payments.Add(payment);
        await PaymentContext.SaveChangesAsync();
        PaymentContext.ChangeTracker.Clear();
        return paymentId;
    }

    private async Task AssertOutboxContainsAsync(string eventTypeName, Guid paymentId, int expectedCount)
    {
        using var scope = Factory.Services.CreateScope();
        var outboxStore = scope.ServiceProvider.GetRequiredService<IOutboxStore>();
        var outboxEvents = await outboxStore.GetUnpublishedOutboxEvents();

        var matching = outboxEvents.Where(e =>
            e.EventType.Contains(eventTypeName, StringComparison.Ordinal) &&
            e.Data.Contains(paymentId.ToString(), StringComparison.OrdinalIgnoreCase)).ToList();

        Assert.Equal(expectedCount, matching.Count);
    }

    private HttpClient CreateCustomerClient(string customerId)
    {
        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.RoleHeader, "Customer");
        client.DefaultRequestHeaders.Add(TestAuthHandler.CustomerIdHeader, customerId);
        return client;
    }
}
