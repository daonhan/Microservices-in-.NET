using ECommerce.Shared.Infrastructure.EventBus;
using ECommerce.Shared.Infrastructure.Outbox;
using Microsoft.Extensions.Options;
using Saga.Service.Domain;
using Saga.Service.Domain.Abstractions;
using Saga.Service.Domain.OrderSaga;
using Saga.Service.Infrastructure.Data.EntityFramework;
using Saga.Service.Infrastructure.Observability;

namespace Saga.Service.Infrastructure.Reaper;

internal sealed partial class SagaReaperService : BackgroundService
{
    private const string OrderSagaType = "Order";

    private readonly TimeSpan _period;
    private readonly int _maxRetries;
    private readonly IServiceScopeFactory _serviceScopeFactory;
    private readonly OrderSagaTimeoutScheduler _timeoutScheduler;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<SagaReaperService> _logger;

    public SagaReaperService(
        IServiceScopeFactory serviceScopeFactory,
        IOptions<SagaReaperOptions> options,
        OrderSagaTimeoutScheduler timeoutScheduler,
        TimeProvider timeProvider,
        ILogger<SagaReaperService> logger)
    {
        _serviceScopeFactory = serviceScopeFactory;
        _timeoutScheduler = timeoutScheduler;
        _timeProvider = timeProvider;
        _logger = logger;
        _period = TimeSpan.FromSeconds(Math.Max(1, options.Value.IntervalInSeconds));
        _maxRetries = Math.Max(0, options.Value.MaxRetries);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using PeriodicTimer timer = new(_period, _timeProvider);

        while (!stoppingToken.IsCancellationRequested && await timer.WaitForNextTickAsync(stoppingToken))
        {
            await RunOnceAsync(stoppingToken);
        }
    }

    internal async Task RunOnceAsync(CancellationToken stoppingToken)
    {
        using var serviceScope = _serviceScopeFactory.CreateScope();

        var sagaStore = serviceScope.ServiceProvider.GetRequiredService<ISagaInstanceStore>();
        var outboxStore = serviceScope.ServiceProvider.GetRequiredService<IOutboxStore>();
        var runner = serviceScope.ServiceProvider.GetRequiredService<IOrderSagaTransitionRunner>();
        var now = _timeProvider.GetUtcNow().UtcDateTime;

        var compensationCandidates = new List<(Guid SagaId, Event Trigger)>();

        await sagaStore.ExecuteAsync(async () =>
        {
            var overdueSagas = await sagaStore.GetOverdueOrderSagas(OrderSagaType, now, stoppingToken);

            foreach (var saga in overdueSagas)
            {
                if (saga.OrderSagaState is null)
                {
                    continue;
                }

                SagaTelemetry.Overdue.Add(1,
                    new KeyValuePair<string, object?>("type", saga.SagaType),
                    new KeyValuePair<string, object?>("step", saga.CurrentStep));

                LogSagaOverdue(_logger, saga.SagaId, saga.SagaType, saga.CurrentStep, saga.RetryCount, _maxRetries);

                if (!Enum.TryParse<OrderSagaStep>(saga.CurrentStep, out var currentStep))
                {
                    MarkFailed(saga, now, "Current saga step could not be parsed.");
                    continue;
                }

                if (saga.RetryCount >= _maxRetries)
                {
                    var trigger = new Event
                    {
                        CorrelationId = saga.CorrelationId,
                        SagaId = saga.SagaId
                    };
                    compensationCandidates.Add((saga.SagaId, trigger));
                    continue;
                }

                if (saga.LastCommandId is not { } commandId
                    || !await outboxStore.RequeueOutboxEvent(commandId))
                {
                    MarkFailed(saga, now, "Timed out command could not be found in the outbox.");
                    continue;
                }

                saga.RetryCount += 1;
                saga.UpdatedAt = now;
                _timeoutScheduler.MoveForward(saga, now);
                saga.Transitions.Add(new SagaTransition
                {
                    SagaId = saga.SagaId,
                    FromStep = currentStep.ToString(),
                    ToStep = currentStep.ToString(),
                    Timestamp = now,
                    TriggerMessageId = commandId,
                    TriggerKind = SagaTriggerKind.Timeout,
                    Error = "Saga step timed out; in-flight command requeued."
                });
            }

            await sagaStore.SaveChangesAsync(stoppingToken);
            return [];
        });

        foreach (var (sagaId, trigger) in compensationCandidates)
        {
            await runner.BeginCompensation(
                sagaId,
                trigger,
                SagaTriggerKind.Timeout,
                "Saga step exceeded max retries; compensation started.",
                stoppingToken);
        }
    }

    private void MarkFailed(SagaInstance saga, DateTime now, string error)
    {
        var fromStep = saga.CurrentStep;
        saga.Status = SagaStatus.Failed;
        saga.NextTimeoutAt = null;
        saga.RetryCount = 0;
        saga.LastCommandId = null;
        saga.UpdatedAt = now;
        saga.Transitions.Add(new SagaTransition
        {
            SagaId = saga.SagaId,
            FromStep = fromStep,
            ToStep = fromStep,
            Timestamp = now,
            TriggerMessageId = Guid.NewGuid(),
            TriggerKind = SagaTriggerKind.Timeout,
            Error = error
        });

        SagaTelemetry.Failed.Add(1,
            new KeyValuePair<string, object?>("type", saga.SagaType),
            new KeyValuePair<string, object?>("reason", error));

        LogSagaTimeoutFailure(_logger, saga.SagaId, saga.SagaType, fromStep, error);
    }

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Saga {SagaId} ({SagaType}) step {Step} is overdue at retry {RetryCount}/{MaxRetries}")]
    private static partial void LogSagaOverdue(
        ILogger logger,
        Guid sagaId,
        string sagaType,
        string step,
        int retryCount,
        int maxRetries);

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "Saga {SagaId} ({SagaType}) step {Step} failed during timeout handling: {Error}")]
    private static partial void LogSagaTimeoutFailure(
        ILogger logger,
        Guid sagaId,
        string sagaType,
        string step,
        string error);
}
