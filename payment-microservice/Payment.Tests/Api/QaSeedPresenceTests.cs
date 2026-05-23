using ECommerce.Shared.Qa;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Payment.Service.Domain;
using Payment.Service.Infrastructure.Data.EntityFramework;

namespace Payment.Tests.Api;

public class QaSeedPresenceTests : IntegrationTestBase
{
    public QaSeedPresenceTests(PaymentWebApplicationFactory webApplicationFactory)
        : base(webApplicationFactory)
    {
    }

    [Fact]
    public async Task QaSeeds_AuthorizedPayment_IsPresentAndCorrect()
    {
        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<PaymentContext>();

        var payment = await context.Payments.SingleOrDefaultAsync(p => p.PaymentId == QaPersonas.PaymentAuthorizedId);

        Assert.NotNull(payment);
        Assert.Equal(QaPersonas.OrderAuthorizedId, payment.OrderId);
        Assert.Equal(QaPersonas.CustomerHappyId.ToString(), payment.CustomerId);
        Assert.Equal(QaPersonas.PaymentHappyAmount, payment.Amount);
        Assert.Equal(PaymentStatus.Authorized, payment.Status);
        Assert.Equal(QaPersonas.PaymentAuthorizedProviderRef, payment.ProviderReference);

        var orderCustomer = await context.OrderCustomers.SingleOrDefaultAsync(oc => oc.OrderId == QaPersonas.OrderAuthorizedId);
        Assert.NotNull(orderCustomer);
        Assert.Equal(QaPersonas.CustomerHappyId.ToString(), orderCustomer.CustomerId);
    }

    [Fact]
    public async Task QaSeeds_CapturedPayment_IsPresentAndCorrect()
    {
        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<PaymentContext>();

        var payment = await context.Payments.SingleOrDefaultAsync(p => p.PaymentId == QaPersonas.PaymentCapturedId);

        Assert.NotNull(payment);
        Assert.Equal(QaPersonas.OrderCapturedId, payment.OrderId);
        Assert.Equal(QaPersonas.CustomerHappyId.ToString(), payment.CustomerId);
        Assert.Equal(QaPersonas.PaymentHappyAmount, payment.Amount);
        Assert.Equal(PaymentStatus.Captured, payment.Status);
        Assert.Equal(QaPersonas.PaymentCapturedProviderRef, payment.ProviderReference);

        var orderCustomer = await context.OrderCustomers.SingleOrDefaultAsync(oc => oc.OrderId == QaPersonas.OrderCapturedId);
        Assert.NotNull(orderCustomer);
        Assert.Equal(QaPersonas.CustomerHappyId.ToString(), orderCustomer.CustomerId);
    }
}
