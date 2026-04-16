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
    private readonly IServiceScopeFactory _serviceScopeFactory;
    private readonly ILogger<OutboxBackgroundService> _logger;

    public OutboxBackgroundService(IServiceScopeFactory serviceScopeFactory,
        IOptions<OutboxOptions> outboxOptions,
        ILogger<OutboxBackgroundService> logger)
    {
        _serviceScopeFactory = serviceScopeFactory;
        _logger = logger;
        _period = TimeSpan.FromSeconds(outboxOptions.Value.PublishIntervalInSeconds);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using PeriodicTimer timer = new(_period);

        while (!stoppingToken.IsCancellationRequested && await timer.WaitForNextTickAsync(stoppingToken))
        {
            LogRetrievingEvents(_logger);

            using var serviceScope = _serviceScopeFactory.CreateScope();

            var outboxStore = serviceScope.ServiceProvider.GetRequiredService<IOutboxStore>();
            var eventBus = serviceScope.ServiceProvider.GetRequiredService<IEventBus>();

            var unpublishedEvents = await outboxStore.GetUnpublishedOutboxEvents();

            foreach (var unpublishedEvent in unpublishedEvents)
            {
                var @event = JsonSerializer.Deserialize(unpublishedEvent.Data,
                    Type.GetType(unpublishedEvent.EventType)!) as Event;

                await eventBus.PublishAsync(@event!);

                await outboxStore.MarkOutboxEventAsPublished(unpublishedEvent.Id);
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
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Retrieving unpublished outbox events")]
    private static partial void LogRetrievingEvents(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Unpublished outbox events sent")]
    private static partial void LogEventsSent(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "No unpublished events to send")]
    private static partial void LogNoEvents(ILogger logger);
}
