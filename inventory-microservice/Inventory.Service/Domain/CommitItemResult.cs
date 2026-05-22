namespace Inventory.Service.Domain;

internal enum CommitOutcome
{
    Committed = 1,
    NothingHeld = 2
}

internal sealed record CommitItemResult(
    CommitOutcome Outcome,
    IReadOnlyList<StockMovement> Movements);
