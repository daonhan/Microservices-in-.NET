namespace ApiGateway.Gateway;

public static class YarpGatewayModule
{
    public static void AddServices(WebApplicationBuilder builder) =>
        throw new NotImplementedException(
            "YARP gateway provider is not yet implemented. Set Gateway:Provider=Ocelot to use the active implementation.");

    public static void UseMiddleware(WebApplication app) =>
        throw new NotImplementedException(
            "YARP gateway provider is not yet implemented. Set Gateway:Provider=Ocelot to use the active implementation.");
}
