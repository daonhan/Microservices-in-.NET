using ECommerce.Shared.Infrastructure.Outbox;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Shipping.Tests;

/// <summary>
/// Variant of <see cref="ShippingWebApplicationFactory"/> that additionally removes
/// the <see cref="OutboxBackgroundService"/> poller.
///
/// Used by tests that assert on the canonical (historical) outbox event stream via
/// <c>GetUnpublishedOutboxEvents()</c>. With the poller running, under parallel-test
/// CPU pressure it can publish and mark early rows <c>Sent</c> before the assertion
/// queries the store, leaving only the tail of the stream (issue #151). Disabling the
/// poller makes "unpublished" == "all emitted" so the audit is deterministic.
/// </summary>
public sealed class ShippingNoOutboxPollerWebApplicationFactory : ShippingWebApplicationFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);

        builder.ConfigureTestServices(services =>
        {
            var descriptors = services
                .Where(descriptor => descriptor.ServiceType == typeof(IHostedService)
                    && descriptor.ImplementationType == typeof(OutboxBackgroundService))
                .ToArray();

            foreach (var descriptor in descriptors)
            {
                services.Remove(descriptor);
            }
        });
    }
}
