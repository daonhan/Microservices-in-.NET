using Auth.Service.Models;

namespace Auth.Service.Services;

public class ServiceClientOptions
{
    public const string SectionName = "ServiceClients";

    public List<ServiceClient> Clients { get; set; } = new();
}
