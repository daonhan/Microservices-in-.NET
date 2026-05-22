using System.Security.Cryptography;
using System.Text;
using Auth.Service.Domain;
using Auth.Service.Domain.Tokens;
using ECommerce.Shared.Observability.Metrics;

namespace Auth.Service.Features.IssueServiceToken;

internal sealed class IssueServiceTokenHandler
{
    private readonly ServiceClientOptions _serviceClients;
    private readonly IServiceTokenService _serviceTokenService;
    private readonly MetricFactory _metricFactory;

    public IssueServiceTokenHandler(ServiceClientOptions serviceClients,
        IServiceTokenService serviceTokenService, MetricFactory metricFactory)
    {
        _serviceClients = serviceClients;
        _serviceTokenService = serviceTokenService;
        _metricFactory = metricFactory;
    }

    public AuthToken? Handle(string clientId, string clientSecret)
    {
        var client = _serviceClients.Clients
            .FirstOrDefault(c => string.Equals(c.ClientId, clientId, StringComparison.Ordinal));

        if (client is null || !SecretsMatch(client.ClientSecret, clientSecret))
        {
            _metricFactory.Counter("service-token-failure", "tokens").Add(1);
            return null;
        }

        var token = _serviceTokenService.GenerateServiceToken(client.ClientId, clientSecret);
        _metricFactory.Counter("service-token-success", "tokens").Add(1);
        return token;
    }

    private static bool SecretsMatch(string expected, string provided)
    {
        var expectedBytes = Encoding.UTF8.GetBytes(expected);
        var providedBytes = Encoding.UTF8.GetBytes(provided);
        return CryptographicOperations.FixedTimeEquals(expectedBytes, providedBytes);
    }
}
