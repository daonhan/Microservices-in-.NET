using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;

namespace ApiGateway.Tests;

internal static class TestServerAddresses
{
    public const string DynamicLoopbackUrl = "http://127.0.0.1:0";

    public static string GetBoundAddress(WebApplication app)
    {
        var addresses = app.Services.GetRequiredService<IServer>()
            .Features
            .Get<IServerAddressesFeature>()
            ?.Addresses;

        return addresses?.SingleOrDefault()
            ?? throw new InvalidOperationException("Test server did not expose a bound address.");
    }
}
