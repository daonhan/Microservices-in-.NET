namespace Auth.Service.Features.IssueServiceToken;

public class ServiceClientOptions
{
    public const string SectionName = "ServiceClients";

    public List<ServiceClient> Clients { get; set; } = new();
}
