using ApiGateway.Gateway;

namespace ApiGateway.Tests.Gateway;

public class GatewayProviderExtensionsTests
{
    [Fact]
    public void AddConfiguredGateway_WithOcelotProvider_RegistersOcelotServices()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Configuration["Gateway:Provider"] = "Ocelot";

        builder.AddConfiguredGateway();

        Assert.Contains(builder.Services, sd =>
            sd.ServiceType.FullName?.StartsWith("Ocelot.", StringComparison.Ordinal) == true);
    }

    [Fact]
    public void AddConfiguredGateway_WithYarpProvider_RegistersYarpServices()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Configuration["Gateway:Provider"] = "Yarp";

        builder.AddConfiguredGateway();

        Assert.Contains(builder.Services, sd =>
            sd.ServiceType.FullName?.StartsWith("Yarp.", StringComparison.Ordinal) == true);
    }

    [Fact]
    public void AddConfiguredGateway_WithYarpProvider_DoesNotRegisterOcelotServices()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Configuration["Gateway:Provider"] = "Yarp";

        builder.AddConfiguredGateway();

        Assert.DoesNotContain(builder.Services, sd =>
            sd.ServiceType.FullName?.StartsWith("Ocelot.", StringComparison.Ordinal) == true);
    }

    [Fact]
    public void AddConfiguredGateway_WithUnknownProvider_ThrowsInvalidOperationException()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Configuration["Gateway:Provider"] = "NonExistentProvider";

        var ex = Assert.Throws<InvalidOperationException>(() => builder.AddConfiguredGateway());

        Assert.Contains("NonExistentProvider", ex.Message, StringComparison.Ordinal);
    }
}
