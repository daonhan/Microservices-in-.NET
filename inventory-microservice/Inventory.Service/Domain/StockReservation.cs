namespace Inventory.Service.Domain;

internal class StockReservation
{
    private ReservationStatus _status = ReservationStatus.Held;

    public long Id { get; set; }

    public Guid OrderId { get; set; }

    public int ProductId { get; set; }

    public int WarehouseId { get; set; }

    public int Quantity { get; set; }

    /// <summary>
    /// The reservation's lifecycle state. The setter is <c>init</c>-only: once a
    /// reservation exists its status can only change through the guarded
    /// <see cref="Commit"/> / <see cref="Release"/> transitions, which the
    /// <see cref="StockItem"/> aggregate invokes. Illegal transitions throw.
    /// </summary>
    public ReservationStatus Status
    {
        get => _status;
        init => _status = value;
    }

    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// Transitions a <see cref="ReservationStatus.Held"/> reservation to
    /// <see cref="ReservationStatus.Committed"/>. Any other source state is illegal.
    /// </summary>
    public void Commit()
    {
        if (_status != ReservationStatus.Held)
        {
            throw new InvalidOperationException(
                $"Cannot commit a reservation in {_status} state; only Held reservations can be committed.");
        }

        _status = ReservationStatus.Committed;
    }

    /// <summary>
    /// Transitions a <see cref="ReservationStatus.Held"/> or
    /// <see cref="ReservationStatus.Committed"/> reservation to
    /// <see cref="ReservationStatus.Released"/>. Releasing an already-released
    /// reservation is illegal — the aggregate guards idempotency by skipping
    /// already-released reservations rather than calling this twice.
    /// </summary>
    public void Release()
    {
        if (_status == ReservationStatus.Released)
        {
            throw new InvalidOperationException("Cannot release a reservation that is already released.");
        }

        _status = ReservationStatus.Released;
    }
}
