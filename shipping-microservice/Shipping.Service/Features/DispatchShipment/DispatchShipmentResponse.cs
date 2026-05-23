namespace Shipping.Service.Features.DispatchShipment;

public record DispatchShipmentResponse(
    Guid ShipmentId,
    string Status,
    string CarrierKey,
    string TrackingNumber,
    string LabelRef,
    decimal QuotedPriceAmount,
    string QuotedPriceCurrency);
