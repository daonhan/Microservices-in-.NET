namespace Shipping.Service.Models;

internal class Shipment
{
    private static readonly ShipmentStatus[] TerminalStates =
    [
        ShipmentStatus.Delivered,
        ShipmentStatus.Cancelled,
        ShipmentStatus.Failed,
        ShipmentStatus.Returned,
    ];

    private readonly List<ShipmentLine> _lines = [];
    private readonly List<ShipmentStatusHistoryEntry> _statusHistory = [];

    public Guid Id { get; private set; }

    public Guid OrderId { get; private set; }

    public string CustomerId { get; private set; } = string.Empty;

    public int WarehouseId { get; private set; }

    public ShipmentStatus Status { get; private set; }

    public DateTime CreatedAt { get; private set; }

    public string? CarrierKey { get; private set; }

    public string? TrackingNumber { get; private set; }

    public string? LabelRef { get; private set; }

    public decimal? QuotedPriceAmount { get; private set; }

    public string? QuotedPriceCurrency { get; private set; }

    public ShippingAddress? ShippingAddress { get; private set; }

    public IReadOnlyCollection<ShipmentLine> Lines => _lines.AsReadOnly();

    public IReadOnlyList<ShipmentStatusHistoryEntry> StatusHistory => _statusHistory.AsReadOnly();

    private Shipment()
    {
    }

    public static Shipment Create(
        Guid id,
        Guid orderId,
        string customerId,
        int warehouseId,
        DateTime createdAt,
        ShipmentStatusSource source = ShipmentStatusSource.System)
    {
        var shipment = new Shipment
        {
            Id = id,
            OrderId = orderId,
            CustomerId = customerId,
            WarehouseId = warehouseId,
            Status = ShipmentStatus.Pending,
            CreatedAt = createdAt,
        };

        shipment._statusHistory.Add(new ShipmentStatusHistoryEntry
        {
            ShipmentId = id,
            Status = ShipmentStatus.Pending,
            OccurredAt = createdAt,
            Source = source,
        });

        return shipment;
    }

    public void AddLine(int productId, int quantity)
    {
        _lines.Add(new ShipmentLine
        {
            ProductId = productId,
            Quantity = quantity,
        });
    }

    public bool IsTerminal => TerminalStates.Contains(Status);

    public bool TryPick(DateTime occurredAt, ShipmentStatusSource source)
        => TryTransition(ShipmentStatus.Picked, [ShipmentStatus.Pending], occurredAt, source, reason: null);

    public bool TryPack(DateTime occurredAt, ShipmentStatusSource source)
        => TryTransition(ShipmentStatus.Packed, [ShipmentStatus.Picked], occurredAt, source, reason: null);

    public bool TryDispatch(DateTime occurredAt, ShipmentStatusSource source)
        => TryTransition(ShipmentStatus.Shipped, [ShipmentStatus.Packed], occurredAt, source, reason: null);

    public bool TryDispatch(
        string carrierKey,
        string trackingNumber,
        string labelRef,
        Money quotedPrice,
        ShippingAddress shippingAddress,
        DateTime occurredAt,
        ShipmentStatusSource source)
    {
        if (string.IsNullOrWhiteSpace(carrierKey))
        {
            throw new ArgumentException("Carrier key required.", nameof(carrierKey));
        }

        if (string.IsNullOrWhiteSpace(trackingNumber))
        {
            throw new ArgumentException("Tracking number required.", nameof(trackingNumber));
        }

        if (string.IsNullOrWhiteSpace(labelRef))
        {
            throw new ArgumentException("Label ref required.", nameof(labelRef));
        }

        ArgumentNullException.ThrowIfNull(quotedPrice);
        ArgumentNullException.ThrowIfNull(shippingAddress);

        if (!TryTransition(ShipmentStatus.Shipped, [ShipmentStatus.Packed], occurredAt, source, reason: null))
        {
            return false;
        }

        CarrierKey = carrierKey;
        TrackingNumber = trackingNumber;
        LabelRef = labelRef;
        QuotedPriceAmount = quotedPrice.Amount;
        QuotedPriceCurrency = quotedPrice.Currency;
        ShippingAddress = shippingAddress;
        return true;
    }

    public bool TryMarkInTransit(DateTime occurredAt, ShipmentStatusSource source)
        => TryTransition(ShipmentStatus.InTransit, [ShipmentStatus.Shipped], occurredAt, source, reason: null);

    public bool TryDeliver(DateTime occurredAt, ShipmentStatusSource source)
        => TryTransition(ShipmentStatus.Delivered, [ShipmentStatus.Shipped, ShipmentStatus.InTransit], occurredAt, source, reason: null);

    public bool TryFail(string reason, DateTime occurredAt, ShipmentStatusSource source)
        => TryTransition(ShipmentStatus.Failed, [ShipmentStatus.Shipped, ShipmentStatus.InTransit], occurredAt, source, reason);

    public bool TryReturn(string reason, DateTime occurredAt, ShipmentStatusSource source)
        => TryTransition(ShipmentStatus.Returned, [ShipmentStatus.Shipped, ShipmentStatus.InTransit], occurredAt, source, reason);

    public bool TryCancel(DateTime occurredAt, ShipmentStatusSource source, string? reason = null)
        => TryTransition(
            ShipmentStatus.Cancelled,
            [ShipmentStatus.Pending, ShipmentStatus.Picked, ShipmentStatus.Packed],
            occurredAt,
            source,
            reason);

    private bool TryTransition(
        ShipmentStatus target,
        ShipmentStatus[] legalFrom,
        DateTime occurredAt,
        ShipmentStatusSource source,
        string? reason)
    {
        if (IsTerminal)
        {
            return false;
        }

        if (!legalFrom.Contains(Status))
        {
            return false;
        }

        Status = target;
        _statusHistory.Add(new ShipmentStatusHistoryEntry
        {
            ShipmentId = Id,
            Status = target,
            OccurredAt = occurredAt,
            Source = source,
            Reason = reason,
        });
        return true;
    }
}
