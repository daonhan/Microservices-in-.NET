using ECommerce.Shared.Infrastructure.DeadLetter;
using ECommerce.Shared.Infrastructure.DeadLetter.Models;

namespace ApiGateway.Features.Operator.ListFailures;

internal sealed class ListFailuresHandler
{
    private readonly IDeadLetterStore _store;

    public ListFailuresHandler(IDeadLetterStore store)
    {
        _store = store;
    }

    public Task<DeadLetterPage> HandleAsync(
        string? service,
        string? eventType,
        DeadLetterStatus? status,
        DateTime? from,
        DateTime? to,
        DeadLetterOrigin? origin,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var filter = new DeadLetterFilter(service, eventType, status, from, to, page, pageSize, origin);
        return _store.ListAsync(filter, cancellationToken);
    }
}
