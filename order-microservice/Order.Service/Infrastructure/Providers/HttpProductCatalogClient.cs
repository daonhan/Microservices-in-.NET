using System.Net;
using System.Net.Http.Json;

namespace Order.Service.Infrastructure.Providers;

public class HttpProductCatalogClient : IProductCatalogClient
{
    private readonly HttpClient _httpClient;

    public HttpProductCatalogClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<decimal?> GetUnitPriceAsync(string productId, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.GetAsync($"/{productId}", cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();

        var product = await response.Content.ReadFromJsonAsync<ProductCatalogResponse>(cancellationToken);
        return product?.Price;
    }

    private sealed record ProductCatalogResponse(int Id, string Name, decimal Price, string ProductType, string? Description);
}
