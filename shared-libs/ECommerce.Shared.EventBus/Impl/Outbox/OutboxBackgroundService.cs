using System.Text.Json;
using ECommerce.Shared.Infrastructure.EventBus;
using ECommerce.Shared.Infrastructure.EventBus.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ECommerce.Shared.Infrastructure.Outbox;

public partial class OutboxBackgroundService : BackgroundService
{
    private readonly TimeSpan _period;
    private readonly int _maxAttempts;
    private readonly IServiceScopeFactory _serviceScopeFactory;
    private readonly ILogger<OutboxBackgroundService> _logger;

    public OutboxBackgroundService(IServiceScopeFactory serviceScopeFactory,
        IOptions<OutboxOptions> outboxOptions,
        ILogger<OutboxBackgroundService> logger)
    {
        _serviceScopeFactory = serviceScopeFactory;
        _logger = logger;
        _period = TimeSpan.FromSeconds(outboxOptions.Value.PublishIntervalInSeconds);
        _maxAttempts = outboxOptions.Value.MaxAttempts;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using PeriodicTimer timer = new(_period);

        while (!stoppingToken.IsCancellationRequested && await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await DispatchPendingAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // A transient database/poll failure must not kill the host.
                LogDispatchFailure(_logger, ex);
            }
        }
    }

    private async Task DispatchPendingAsync(CancellationToken stoppingToken)
    {
        LogRetrievingEvents(_logger);

        using var serviceScope = _serviceScopeFactory.CreateScope();

        var outboxStore = serviceScope.ServiceProvider.GetRequiredService<IOutboxStore>();
        var eventBus = serviceScope.ServiceProvider.GetRequiredService<IEventBus>();

        var unpublishedEvents = await outboxStore.GetUnpublishedOutboxEvents();

        foreach (var unpublishedEvent in unpublishedEvents)
        {
            stoppingToken.ThrowIfCancellationRequested();
            if (outboxStore is IOutboxDispatchStore dispatcher)
            {
                await dispatcher.DispatchAsync(unpublishedEvent.Id, async row =>
                {
                    try
                    {
                        await PublishAsync(row, eventBus);
                    }
                    catch (Exception ex)
                    {
                        LogPublishFailure(_logger, row.Id, ex);
                        throw;
                    }
                }, _maxAttempts, stoppingToken);
                continue;
            }

            try
            {
                await PublishAsync(unpublishedEvent, eventBus);

                await outboxStore.MarkOutboxEventAsPublished(unpublishedEvent.Id);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                LogPublishFailure(_logger, unpublishedEvent.Id, ex);
                await outboxStore.RecordPublishFailure(unpublishedEvent.Id, ex.ToString(), _maxAttempts);
            }
        }

        if (unpublishedEvents.Count != 0)
        {
            LogEventsSent(_logger);
        }
        else
        {
            LogNoEvents(_logger);
        }
    }

    private static Task PublishAsync(Models.OutboxEvent row, IEventBus eventBus)
    {
        var type = Type.GetType(row.EventType, throwOnError: true)!;
        var @event = JsonSerializer.Deserialize(row.Data, type) as Event
            ?? throw new InvalidOperationException($"Outbox event {row.Id} did not deserialize to an Event.");
        return eventBus.PublishAsync(@event);
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Outbox dispatch failed; retrying on the next poll")]
    private static partial void LogDispatchFailure(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Information, Message = "Retrieving unpublished outbox events")]
    private static partial void LogRetrievingEvents(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Unpublished outbox events sent")]
    private static partial void LogEventsSent(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "No unpublished events to send")]
    private static partial void LogNoEvents(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to publish outbox event {OutboxEventId}")]
    private static partial void LogPublishFailure(ILogger logger, Guid outboxEventId, Exception exception);
}
