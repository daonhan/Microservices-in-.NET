using Shipping.Service.Models;

namespace Shipping.Service.Carriers;

public record ShipmentQuoteRequest(
    Guid ShipmentId,
    int WarehouseId,
    ShippingAddress Destination,
    int TotalQuantity);

public record CarrierQuote(
    string CarrierKey,
    string CarrierName,
    Money Price,
    int EstimatedDeliveryDays);

public record ShipmentDispatchRequest(
    Guid ShipmentId,
    int WarehouseId,
    ShippingAddress Destination,
    int TotalQuantity);

public record CarrierDispatchResult(
    string TrackingNumber,
    string LabelRef);

public enum CarrierStatusCode
{
    Unknown = 0,
    Accepted = 1,
    InTransit = 2,
    Delivered = 3,
    Failed = 4,
}

public record CarrierStatus(CarrierStatusCode Code, string? Detail);

public interface ICarrierGateway
{
    string CarrierKey { get; }

    string CarrierName { get; }

    Task<CarrierQuote> QuoteAsync(ShipmentQuoteRequest request, CancellationToken cancellationToken = default);

    Task<CarrierDispatchResult> DispatchAsync(ShipmentDispatchRequest request, CancellationToken cancellationToken = default);

    Task<CarrierStatus> GetStatusAsync(string trackingNumber, CancellationToken cancellationToken = default);
}
