namespace Shipping.Service.ApiModels;

public record CarrierQuoteResponse(
    string CarrierKey,
    string CarrierName,
    decimal PriceAmount,
    string PriceCurrency,
    int EstimatedDeliveryDays);
