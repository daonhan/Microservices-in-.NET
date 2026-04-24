using Shipping.Service.Models;

namespace Shipping.Service.Infrastructure.Data;

internal interface IShipmentStore
{
    Task<IReadOnlyList<Shipment>> GetByOrder(Guid orderId);

    Task<Shipment?> GetById(Guid shipmentId);

    Task<IReadOnlyList<Shipment>> ListShipments(ShipmentListFilters filters);

    Task<CreateShipmentsResult> CreateShipmentsForOrder(
        Guid orderId,
        string customerId,
        IReadOnlyList<CreateShipmentLine> lines);

    Task RecordOrderConfirmation(Guid orderId, string customerId);

    Task<string?> TryGetOrderCustomer(Guid orderId);

    Task<int> SaveChangesAsync();
}

internal record CreateShipmentLine(int ProductId, int WarehouseId, int Quantity);

internal record CreateShipmentsResult(bool Created, IReadOnlyList<Shipment> Shipments);

internal record ShipmentListFilters(
    ShipmentStatus? Status,
    int? WarehouseId,
    DateTime? From,
    DateTime? To,
    int Skip,
    int Take);
