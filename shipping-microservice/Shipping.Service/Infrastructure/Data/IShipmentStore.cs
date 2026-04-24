using Shipping.Service.Models;

namespace Shipping.Service.Infrastructure.Data;

internal interface IShipmentStore
{
    Task<IReadOnlyList<Shipment>> GetByOrder(Guid orderId);

    Task<CreateShipmentsResult> CreateShipmentsForOrder(
        Guid orderId,
        string customerId,
        IReadOnlyList<CreateShipmentLine> lines);

    Task RecordOrderConfirmation(Guid orderId, string customerId);

    Task<string?> TryGetOrderCustomer(Guid orderId);
}

internal record CreateShipmentLine(int ProductId, int WarehouseId, int Quantity);

internal record CreateShipmentsResult(bool Created, IReadOnlyList<Shipment> Shipments);
