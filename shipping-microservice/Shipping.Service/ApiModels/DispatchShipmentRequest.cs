namespace Shipping.Service.ApiModels;

public record ShippingAddressDto(
    string Recipient,
    string Line1,
    string? Line2,
    string City,
    string? State,
    string PostalCode,
    string Country);

public record DispatchShipmentRequest(
    string CarrierKey,
    ShippingAddressDto ShippingAddress,
    CarrierQuoteOverride? OverrideQuote);

public record CarrierQuoteOverride(
    decimal PriceAmount,
    string PriceCurrency,
    int EstimatedDeliveryDays);

public record CarrierQuoteResponse(
    string CarrierKey,
    string CarrierName,
    decimal PriceAmount,
    string PriceCurrency,
    int EstimatedDeliveryDays);

public record DispatchShipmentResponse(
    Guid ShipmentId,
    string Status,
    string CarrierKey,
    string TrackingNumber,
    string LabelRef,
    decimal QuotedPriceAmount,
    string QuotedPriceCurrency);
