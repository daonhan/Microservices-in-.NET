using System.Collections.Concurrent;

namespace Shipping.Service.Carriers;

/// <summary>
/// In-memory registry used by fake carriers to track when a tracking number was
/// dispatched so that <see cref="ICarrierGateway.GetStatusAsync"/> can return a
/// deterministic, time-based status progression during polling tests and demos.
/// </summary>
internal sealed class FakeCarrierDispatchRegistry
{
    private readonly ConcurrentDictionary<string, DateTimeOffset> _dispatchedAt = new();

    public void Record(string trackingNumber, DateTimeOffset dispatchedAt)
    {
        if (string.IsNullOrWhiteSpace(trackingNumber))
        {
            return;
        }

        _dispatchedAt[trackingNumber] = dispatchedAt;
    }

    public bool TryGet(string trackingNumber, out DateTimeOffset dispatchedAt)
    {
        if (string.IsNullOrWhiteSpace(trackingNumber))
        {
            dispatchedAt = default;
            return false;
        }

        return _dispatchedAt.TryGetValue(trackingNumber, out dispatchedAt);
    }
}
