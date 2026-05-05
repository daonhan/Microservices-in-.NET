using ECommerce.Shared.Infrastructure.DeadLetter;
using ECommerce.Shared.Infrastructure.DeadLetter.Models;
using Microsoft.EntityFrameworkCore;

namespace ApiGateway.Operator.OutboxPolling;

public sealed partial class OutboxFailurePoller : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOutboxFailureClient _client;
    private readonly OutboxPollerOptions _options;
    private readonly ILogger<OutboxFailurePoller> _logger;

    public OutboxFailurePoller(
        IServiceScopeFactory scopeFactory,
        IOutboxFailureClient client,
        OutboxPollerOptions options,
        ILogger<OutboxFailurePoller> logger)
    {
        _scopeFactory = scopeFactory;
        _client = client;
        _options = options;
        _logger = logger;
    }

    [LoggerMessage(EventId = 1, Level = LogLevel.Information, Message = "Outbox failure poller disabled by configuration.")]
    private partial void LogPollerDisabled();

    [LoggerMessage(EventId = 2, Level = LogLevel.Error, Message = "Outbox failure poll cycle failed.")]
    private partial void LogPollCycleFailed(Exception exception);

    [LoggerMessage(EventId = 3, Level = LogLevel.Warning, Message = "Failed to pull outbox failures from {Service}.")]
    private partial void LogServicePollFailed(Exception exception, string service);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            LogPollerDisabled();
            return;
        }

        var interval = TimeSpan.FromSeconds(Math.Max(1, _options.IntervalSeconds));

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await PollOnceAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogPollCycleFailed(ex);
            }

            try
            {
                await Task.Delay(interval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    public async Task PollOnceAsync(CancellationToken cancellationToken)
    {
        if (_options.Services.Count == 0)
        {
            return;
        }

        var token = await _client.GetServiceTokenAsync(cancellationToken);

        foreach (var service in _options.Services)
        {
            try
            {
                var failures = await _client.GetFailedAsync(service.FailedOutboxEndpoint, token, cancellationToken);
                await UpsertAsync(service.Name, failures, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogServicePollFailed(ex, service.Name);
            }
        }
    }

    private async Task UpsertAsync(
        string serviceName,
        IReadOnlyList<OutboxFailureItem> failures,
        CancellationToken cancellationToken)
    {
        if (failures.Count == 0)
        {
            return;
        }

        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<DeadLetterDbContext>();

        foreach (var failure in failures)
        {
            var existing = await dbContext.DeadLetterMessages
                .FirstOrDefaultAsync(
                    x => x.Origin == DeadLetterOrigin.Outbox
                         && x.Service == serviceName
                         && x.Id == failure.Id,
                    cancellationToken);

            if (existing is null)
            {
                dbContext.DeadLetterMessages.Add(new DeadLetterMessage
                {
                    Id = failure.Id,
                    EventType = failure.EventType,
                    RoutingKey = failure.EventType,
                    OriginalQueue = serviceName,
                    Service = serviceName,
                    Payload = failure.Data,
                    FailureReason = failure.LastError ?? string.Empty,
                    Attempts = failure.Attempts,
                    FailedAt = failure.LastAttemptAt ?? DateTime.UtcNow,
                    Status = DeadLetterStatus.Pending,
                    Origin = DeadLetterOrigin.Outbox,
                    CorrelationId = failure.CorrelationId,
                });
            }
            else
            {
                existing.Attempts = failure.Attempts;
                existing.FailureReason = failure.LastError ?? string.Empty;
                existing.FailedAt = failure.LastAttemptAt ?? existing.FailedAt;
                existing.Payload = failure.Data;
                existing.CorrelationId = failure.CorrelationId;
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
