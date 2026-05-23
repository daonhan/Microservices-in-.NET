namespace Shipping.Service.Features.GetCarrierQuotes;

public record CarrierQuoteResponse(
    string CarrierKey,
    string CarrierName,
    decimal PriceAmount,
    string PriceCurrency,
    int EstimatedDeliveryDays);
