using ECommerce.Shared.Infrastructure.DeadLetter.Models;

namespace ECommerce.Shared.Infrastructure.DeadLetter;

public interface IDeadLetterStore
{
    Task CaptureAsync(DeadLetterMessage message, CancellationToken cancellationToken = default);

    Task<DeadLetterPage> ListAsync(DeadLetterFilter filter, CancellationToken cancellationToken = default);

    Task<DeadLetterMessage?> GetAsync(Guid id, CancellationToken cancellationToken = default);
}

public sealed record DeadLetterFilter(
    string? Service = null,
    string? EventType = null,
    DeadLetterStatus? Status = null,
    DateTime? From = null,
    DateTime? To = null,
    int Page = 1,
    int PageSize = 50);

public sealed record DeadLetterPage(
    IReadOnlyList<DeadLetterMessage> Items,
    int Page,
    int PageSize,
    int TotalCount);
