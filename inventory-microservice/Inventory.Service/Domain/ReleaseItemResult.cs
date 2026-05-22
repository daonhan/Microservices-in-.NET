namespace Inventory.Service.Domain;

internal enum ReleaseOutcome
{
    Released = 1,
    NothingToRelease = 2
}

internal sealed record ReleaseItemResult(
    ReleaseOutcome Outcome,
    IReadOnlyList<StockMovement> Movements);
