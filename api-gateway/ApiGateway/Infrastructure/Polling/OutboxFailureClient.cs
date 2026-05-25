using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ApiGateway.Infrastructure.Polling;

public interface IOutboxFailureClient
{
    Task<string> GetServiceTokenAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<OutboxFailureItem>> GetFailedAsync(
        string endpoint,
        string token,
        CancellationToken cancellationToken);
}

public sealed class OutboxFailureClient : IOutboxFailureClient
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _httpClient;
    private readonly OutboxPollerOptions _options;

    public OutboxFailureClient(HttpClient httpClient, OutboxPollerOptions options)
    {
        _httpClient = httpClient;
        _options = options;
    }

    public async Task<string> GetServiceTokenAsync(CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, _options.AuthTokenEndpoint)
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials",
                ["client_id"] = _options.ClientId,
                ["client_secret"] = _options.ClientSecret,
            }),
        };

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<TokenResponse>(SerializerOptions, cancellationToken);
        if (payload is null || string.IsNullOrEmpty(payload.Token))
        {
            throw new InvalidOperationException("Auth service returned an empty service token.");
        }

        return payload.Token;
    }

    public async Task<IReadOnlyList<OutboxFailureItem>> GetFailedAsync(
        string endpoint,
        string token,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var items = await response.Content.ReadFromJsonAsync<List<OutboxFailureItem>>(SerializerOptions, cancellationToken);
        return items ?? new List<OutboxFailureItem>();
    }

    private sealed record TokenResponse(
        [property: JsonPropertyName("token")] string Token,
        [property: JsonPropertyName("expiresIn")] int ExpiresIn);
}
