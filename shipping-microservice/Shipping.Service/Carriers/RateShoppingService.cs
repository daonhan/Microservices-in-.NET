using Shipping.Service.Models;

namespace Shipping.Service.Carriers;

internal sealed class RateShoppingService
{
    private readonly IEnumerable<ICarrierGateway> _carriers;

    public RateShoppingService(IEnumerable<ICarrierGateway> carriers)
    {
        _carriers = carriers;
    }

    public async Task<IReadOnlyList<CarrierQuote>> GetRankedQuotesAsync(
        ShipmentQuoteRequest request,
        CancellationToken cancellationToken = default)
    {
        var quotes = new List<CarrierQuote>();
        foreach (var carrier in _carriers)
        {
            quotes.Add(await carrier.QuoteAsync(request, cancellationToken));
        }

        // Rank by cheapest, tiebreak fastest.
        return quotes
            .OrderBy(q => q.Price.Amount)
            .ThenBy(q => q.EstimatedDeliveryDays)
            .ToList();
    }

    public ICarrierGateway? FindCarrier(string carrierKey)
        => _carriers.FirstOrDefault(c => string.Equals(c.CarrierKey, carrierKey, StringComparison.OrdinalIgnoreCase));
}
