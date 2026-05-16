using Inventory.Service.Models;

namespace Inventory.Tests.Domain;

public class StockReservationTests
{
    [Fact]
    public void Commit_FromHeld_TransitionsToCommitted()
    {
        var reservation = new StockReservation { Status = ReservationStatus.Held };

        reservation.Commit();

        Assert.Equal(ReservationStatus.Committed, reservation.Status);
    }

    [Fact]
    public void Commit_FromCommitted_IsRejected()
    {
        var reservation = new StockReservation { Status = ReservationStatus.Committed };

        Assert.Throws<InvalidOperationException>(reservation.Commit);
        Assert.Equal(ReservationStatus.Committed, reservation.Status);
    }

    [Fact]
    public void Commit_FromReleased_IsRejected()
    {
        var reservation = new StockReservation { Status = ReservationStatus.Released };

        Assert.Throws<InvalidOperationException>(reservation.Commit);
        Assert.Equal(ReservationStatus.Released, reservation.Status);
    }

    [Fact]
    public void Release_FromHeld_TransitionsToReleased()
    {
        var reservation = new StockReservation { Status = ReservationStatus.Held };

        reservation.Release();

        Assert.Equal(ReservationStatus.Released, reservation.Status);
    }

    [Fact]
    public void Release_FromCommitted_TransitionsToReleased()
    {
        var reservation = new StockReservation { Status = ReservationStatus.Committed };

        reservation.Release();

        Assert.Equal(ReservationStatus.Released, reservation.Status);
    }

    [Fact]
    public void Release_FromReleased_IsRejected()
    {
        var reservation = new StockReservation { Status = ReservationStatus.Released };

        Assert.Throws<InvalidOperationException>(reservation.Release);
        Assert.Equal(ReservationStatus.Released, reservation.Status);
    }
}
