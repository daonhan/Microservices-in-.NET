using System.ComponentModel.DataAnnotations;

namespace ApiGateway.Infrastructure.Proxy;

public class GatewayProviderOptions
{
    public const string SectionName = "Gateway";

    [Required]
    public string Provider { get; set; } = GatewayProvider.Ocelot.ToString();
}
