using System.ComponentModel.DataAnnotations;

namespace ApiGateway.Gateway;

public class GatewayProviderOptions
{
    public const string SectionName = "Gateway";

    [Required]
    public string Provider { get; set; } = GatewayProvider.Ocelot.ToString();
}
