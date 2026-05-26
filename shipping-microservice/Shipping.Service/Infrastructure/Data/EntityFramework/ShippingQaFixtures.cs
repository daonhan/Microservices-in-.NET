using ECommerce.Shared.Qa;

namespace Shipping.Service.Infrastructure.Data.EntityFramework;

/// <summary>
/// Phase 3b shipping admin-ops seed fixtures. Five shipments owned by
/// <see cref="QaPersonas.CustomerHappyId"/>, one per non-trivial status,
/// so each admin transition is one Bruno request away.
/// </summary>
internal static class ShippingQaFixtures
{
    public static readonly DateTime SeedEpoch = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    public static readonly Guid ShipmentPickPendingId = new("c0000000-0000-0000-0000-000000000001");
    public static readonly Guid ShipmentPickedId = new("c0000000-0000-0000-0000-000000000002");
    public static readonly Guid ShipmentPackedId = new("c0000000-0000-0000-0000-000000000003");
    public static readonly Guid ShipmentDispatchedId = new("c0000000-0000-0000-0000-000000000004");
    public static readonly Guid ShipmentCancelPendingId = new("c0000000-0000-0000-0000-000000000005");
    public static readonly Guid ShipmentFailDispatchedId = new("c0000000-0000-0000-0000-000000000006");
    public static readonly Guid ShipmentReturnDispatchedId = new("c0000000-0000-0000-0000-000000000007");

    public static readonly Guid ShippingOrderPickPendingId = new("d0000000-0000-0000-0000-000000000001");
    public static readonly Guid ShippingOrderPickedId = new("d0000000-0000-0000-0000-000000000002");
    public static readonly Guid ShippingOrderPackedId = new("d0000000-0000-0000-0000-000000000003");
    public static readonly Guid ShippingOrderDispatchedId = new("d0000000-0000-0000-0000-000000000004");
    public static readonly Guid ShippingOrderCancelPendingId = new("d0000000-0000-0000-0000-000000000005");
    public static readonly Guid ShippingOrderFailDispatchedId = new("d0000000-0000-0000-0000-000000000006");
    public static readonly Guid ShippingOrderReturnDispatchedId = new("d0000000-0000-0000-0000-000000000007");

    public const string DispatchedCarrierKey = "fake-ground";
    public const string DispatchedTrackingNumber = "QA-TRACK-DISPATCHED-001";
    public const string DispatchedLabelRef = "label://qa/QA-TRACK-DISPATCHED-001";
    public const decimal DispatchedQuotedAmount = 5.00m;
    public const string DispatchedQuotedCurrency = "USD";

    public const string FailDispatchedTrackingNumber = "QA-TRACK-DISPATCHED-FAIL-001";
    public const string FailDispatchedLabelRef = "label://qa/QA-TRACK-DISPATCHED-FAIL-001";

    public const string ReturnDispatchedTrackingNumber = "QA-TRACK-DISPATCHED-RETURN-001";
    public const string ReturnDispatchedLabelRef = "label://qa/QA-TRACK-DISPATCHED-RETURN-001";

    public const int LineSeedIdStart = 90001;
    public const int HistorySeedIdStart = 90001;
}
