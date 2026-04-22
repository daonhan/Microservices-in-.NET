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
    public void AddConfiguredGateway_WithYarpProvider_ThrowsNotImplementedException()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Configuration["Gateway:Provider"] = "Yarp";

        var ex = Assert.Throws<NotImplementedException>(() => builder.AddConfiguredGateway());

        Assert.Contains("YARP", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AddConfiguredGateway_WithYarpProvider_DoesNotRegisterOcelotServices()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Configuration["Gateway:Provider"] = "Yarp";

        Assert.Throws<NotImplementedException>(() => builder.AddConfiguredGateway());

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
