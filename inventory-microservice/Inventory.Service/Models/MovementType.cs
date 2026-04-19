namespace Inventory.Service.Models;

internal enum MovementType
{
    Reserve = 1,
    Commit = 2,
    Release = 3,
    Restock = 4,
    Adjustment = 5
}
