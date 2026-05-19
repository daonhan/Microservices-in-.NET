using System.Net;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace AppHost.Tests;

// Phase 3 smoke test (issue #149): boots the full Aspire local-dev graph and
// proves every wired resource serves /health/ready 200. Guards against missing
// project references, broken connection-string handoff, and any service that
// fails to start under Aspire's environment. CI-only — the Husky.Net
// pre-commit hook still runs Basket tests only (repo policy).
public class AppHostSmokeTests
{
    // Every service + the gateway, by their AppHost resource names.
    private static readonly string[] Resources =
        ["auth", "basket", "product", "order", "inventory", "payment", "shipping", "saga", "gateway"];

    [Fact]
    public async Task Given_AppHost_When_Started_Then_All_Services_Are_Healthy()
    {
        // Bounded overall budget so a broken wiring fails the pipeline instead
        // of hanging indefinitely on a hosted agent.
        using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(10));
        var ct = cts.Token;

        var appHost = await DistributedApplicationTestingBuilder
            .CreateAsync<Projects.Nhamnhi_AppHost>(ct);

        await using var app = await appHost.BuildAsync(ct);
        await app.StartAsync(ct);

        var notification = app.Services.GetRequiredService<ResourceNotificationService>();

        foreach (var resource in Resources)
        {
            await notification.WaitForResourceHealthyAsync(resource, ct);

            using var client = app.CreateHttpClient(resource);
            var response = await GetReadyAsync(client, ct);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
    }

    // The Aspire "healthy" signal can briefly precede the app serving traffic;
    // retry /health/ready until it answers 200 or the overall budget expires.
    private static async Task<HttpResponseMessage> GetReadyAsync(
        HttpClient client, CancellationToken ct)
    {
        HttpResponseMessage? last = null;
        while (!ct.IsCancellationRequested)
        {
            try
            {
                last = await client.GetAsync("/health/ready", ct);
                if (last.StatusCode == HttpStatusCode.OK)
                {
                    return last;
                }
            }
            catch (HttpRequestException)
            {
                // Endpoint not accepting connections yet; retry within budget.
            }

            await Task.Delay(TimeSpan.FromSeconds(2), ct);
        }

        return last ?? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
    }
}
