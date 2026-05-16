namespace Inventory.Service.Models;

internal enum HoldOutcome
{
    Held = 1,
    AlreadyHeld = 2,
    InsufficientStock = 3
}

internal sealed record HoldResult(
    HoldOutcome Outcome,
    int ProductId,
    int WarehouseId,
    int Quantity,
    int Available,
    StockReservation? Reservation,
    StockMovement? Movement);
