using System.Diagnostics;
using System.Diagnostics.Metrics;
using ECommerce.Shared.Infrastructure.EventBus.Abstractions;
using ECommerce.Shared.Infrastructure.Outbox;
using ECommerce.Shared.IntegrationEvents.Commands;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Saga.Service.Infrastructure.Data.EntityFramework;
using Saga.Service.IntegrationEvents;
using Saga.Service.IntegrationEvents.EventHandlers;
using Saga.Service.Models;
using Saga.Service.Observability;

namespace Saga.Tests.Api;

public class SagaObservabilityTests : IClassFixture<SagaWebApplicationFactory>
{
    private readonly SagaWebApplicationFactory _factory;

    public SagaObservabilityTests(SagaWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Given_OrchestratedOrder_When_SagaOpens_Then_StartedCounterAndTransitionSpanAreEmitted()
    {
        var orderId = Guid.NewGuid();
        var orderCreated = CreateOrderCreated(orderId);

        using var metrics = new MetricCapture();
        using var spans = new SpanCapture();

        await OpenSaga(orderCreated);

        var started = Assert.Single(metrics.Measurements("saga_started_total"));
        Assert.Equal(1, started.Value);
        Assert.Equal("Order", started.Tag("type"));

        var span = Assert.Single(spans.Stopped, a => a.OperationName == "saga.transition");
        Assert.IsType<Guid>(span.GetTagItem("saga.id"));
        Assert.NotEqual(Guid.Empty, (Guid)span.GetTagItem("saga.id")!);
        Assert.Equal("Order", span.GetTagItem("saga.type"));
        Assert.Equal(OrderSagaStep.Started.ToString(), span.GetTagItem("saga.from_step"));
        Assert.Equal(OrderSagaStep.StockReserving.ToString(), span.GetTagItem("saga.to_step"));
    }

    [Fact]
    public async Task Given_OpenSaga_When_HappyPathCompletes_Then_CompletedCounterOnceAndStepDurationRecorded()
    {
        var orderId = Guid.NewGuid();
        var orderCreated = CreateOrderCreated(orderId);

        var reserveCommandId = await OpenSaga(orderCreated);
        var sagaId = await GetSagaIdAsync(orderId);

        using var metrics = new MetricCapture();

        var stockReserved = new StockReservedEvent(
            orderId, [new ReservedItem(101, 1, 2)], 25m, "USD")
        {
            CausationId = reserveCommandId,
            SagaId = sagaId,
        };
        await DispatchAsync<StockReservedEventHandler, StockReservedEvent>(stockReserved);

        var authorizeCommandId = await GetLatestCommandIdAsync(nameof(AuthorizePaymentCommand));
        await DispatchAsync<PaymentAuthorizedEventHandler, PaymentAuthorizedEvent>(
            new PaymentAuthorizedEvent(Guid.NewGuid(), orderId, "customer-1", 25m, "USD")
            {
                CausationId = authorizeCommandId,
                SagaId = sagaId,
            });

        var confirmCommandId = await GetLatestCommandIdAsync(nameof(ConfirmOrderCommand));
        await DispatchAsync<OrderConfirmedEventHandler, OrderConfirmedEvent>(
            new OrderConfirmedEvent(orderId, "customer-1")
            {
                CausationId = confirmCommandId,
                SagaId = sagaId,
            });

        var commitCommandId = await GetLatestCommandIdAsync(nameof(CommitStockCommand));
        await DispatchAsync<StockCommittedEventHandler, StockCommittedEvent>(
            new StockCommittedEvent(orderId, [new CommittedItem(101, 1, 2)])
            {
                CausationId = commitCommandId,
                SagaId = sagaId,
            });

        var createShipmentCommandId = await GetLatestCommandIdAsync(nameof(CreateShipmentCommand));
        await DispatchAsync<ShipmentCreatedEventHandler, ShipmentCreatedEvent>(
            new ShipmentCreatedEvent(Guid.NewGuid(), orderId, "customer-1", 1, [new ShipmentLineItem(101, 2)])
            {
                CausationId = createShipmentCommandId,
                SagaId = sagaId,
            });

        var completed = Assert.Single(metrics.Measurements("saga_completed_total"));
        Assert.Equal(1, completed.Value);
        Assert.Equal("Order", completed.Tag("type"));

        var stepDurations = metrics.Measurements("saga_step_duration_seconds");
        Assert.NotEmpty(stepDurations);
        Assert.All(stepDurations, m => Assert.Equal("Order", m.Tag("type")));
        Assert.Contains(stepDurations, m =>
            (string?)m.Tag("step") == OrderSagaStep.StockReserving.ToString());
    }

    [Fact]
    public async Task Given_OpenSaga_When_PaymentFails_Then_CompensationCounterIsEmitted()
    {
        var orderId = Guid.NewGuid();
        var orderCreated = CreateOrderCreated(orderId);

        var reserveCommandId = await OpenSaga(orderCreated);
        var sagaId = await GetSagaIdAsync(orderId);

        var stockReserved = new StockReservedEvent(
            orderId, [new ReservedItem(101, 1, 2)], 25m, "USD")
        {
            CausationId = reserveCommandId,
            SagaId = sagaId,
        };
        await DispatchAsync<StockReservedEventHandler, StockReservedEvent>(stockReserved);

        var authorizeCommandId = await GetLatestCommandIdAsync(nameof(AuthorizePaymentCommand));

        using var metrics = new MetricCapture();

        await DispatchAsync<PaymentFailedEventHandler, PaymentFailedEvent>(
            new PaymentFailedEvent(Guid.NewGuid(), orderId, "customer-1", "Declined")
            {
                CausationId = authorizeCommandId,
                SagaId = sagaId,
            });

        var compensation = Assert.Single(metrics.Measurements("saga_compensation_total"));
        Assert.Equal(1, compensation.Value);
        Assert.Equal("Order", compensation.Tag("type"));
    }

    [Fact]
    public async Task Given_OpenSaga_When_StockReservationFails_Then_FailedCounterWithReasonIsEmitted()
    {
        var orderId = Guid.NewGuid();
        var orderCreated = CreateOrderCreated(orderId);

        var reserveCommandId = await OpenSaga(orderCreated);
        var sagaId = await GetSagaIdAsync(orderId);

        using var metrics = new MetricCapture();

        await DispatchAsync<StockReservationFailedEventHandler, StockReservationFailedEvent>(
            new StockReservationFailedEvent(orderId, [new FailedItem(101, 2, 0)])
            {
                CausationId = reserveCommandId,
                SagaId = sagaId,
            });

        var failed = Assert.Single(metrics.Measurements("saga_failed_total"));
        Assert.Equal(1, failed.Value);
        Assert.Equal("Order", failed.Tag("type"));
        Assert.False(string.IsNullOrWhiteSpace((string?)failed.Tag("reason")));
    }

    private async Task<Guid> OpenSaga(OrderCreatedEvent orderCreated)
    {
        using var scope = _factory.Services.CreateScope();
        var handler = ActivatorUtilities.CreateInstance<OrderCreatedEventHandler>(
            scope.ServiceProvider,
            Options.Create(new SagaOrchestratorOptions
            {
                Enabled = true,
                AllowList = [orderCreated.OrderId]
            }));
        await handler.Handle(orderCreated);

        var outboxStore = scope.ServiceProvider.GetRequiredService<IOutboxStore>();
        var outboxEvents = await outboxStore.GetUnpublishedOutboxEvents();
        return outboxEvents.Single(e =>
            e.EventType.Contains(nameof(ReserveStockCommand), StringComparison.Ordinal)
            && e.Data.Contains(orderCreated.OrderId.ToString(), StringComparison.Ordinal)).Id;
    }

    private async Task<Guid> GetSagaIdAsync(Guid orderId)
    {
        using var scope = _factory.Services.CreateScope();
        var sagaContext = scope.ServiceProvider.GetRequiredService<SagaContext>();
        return await sagaContext.OrderSagaStates
            .Where(s => s.OrderId == orderId)
            .Select(s => s.SagaId)
            .SingleAsync();
    }

    private async Task<Guid> GetLatestCommandIdAsync(string commandTypeName)
    {
        using var scope = _factory.Services.CreateScope();
        var outboxStore = scope.ServiceProvider.GetRequiredService<IOutboxStore>();
        var outboxEvents = await outboxStore.GetUnpublishedOutboxEvents();
        return outboxEvents.Last(e =>
            e.EventType.Contains(commandTypeName, StringComparison.Ordinal)).Id;
    }

    private async Task DispatchAsync<THandler, TEvent>(TEvent @event)
        where THandler : IEventHandler<TEvent>
        where TEvent : ECommerce.Shared.Infrastructure.EventBus.Event
    {
        using var scope = _factory.Services.CreateScope();
        var handler = ActivatorUtilities.CreateInstance<THandler>(scope.ServiceProvider);
        await handler.Handle(@event);
    }

    private static OrderCreatedEvent CreateOrderCreated(Guid orderId) =>
        new(orderId, "customer-1", [new OrderItem("101", 2, 12.50m)], "USD")
        {
            CorrelationId = Guid.NewGuid()
        };

    private sealed class SpanCapture : IDisposable
    {
        private readonly ActivityListener _listener;
        public List<Activity> Stopped { get; } = [];

        public SpanCapture()
        {
            _listener = new ActivityListener
            {
                ShouldListenTo = source => source.Name == SagaTelemetry.ActivitySourceName,
                Sample = (ref ActivityCreationOptions<ActivityContext> _) =>
                    ActivitySamplingResult.AllDataAndRecorded,
                ActivityStopped = activity =>
                {
                    lock (Stopped)
                    {
                        Stopped.Add(activity);
                    }
                },
            };
            ActivitySource.AddActivityListener(_listener);
        }

        public void Dispose() => _listener.Dispose();
    }

    private sealed class MetricCapture : IDisposable
    {
        private readonly MeterListener _listener;
        private readonly List<Measurement> _measurements = [];

        public MetricCapture()
        {
            _listener = new MeterListener
            {
                InstrumentPublished = (instrument, listener) =>
                {
                    if (instrument.Meter.Name == SagaTelemetry.MeterName)
                    {
                        listener.EnableMeasurementEvents(instrument);
                    }
                },
            };
            _listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) =>
                Record(instrument.Name, value, tags));
            _listener.SetMeasurementEventCallback<double>((instrument, value, tags, _) =>
                Record(instrument.Name, value, tags));
            _listener.Start();
        }

        private void Record(string name, double value, ReadOnlySpan<KeyValuePair<string, object?>> tags)
        {
            var copied = new Dictionary<string, object?>(StringComparer.Ordinal);
            foreach (var tag in tags)
            {
                copied[tag.Key] = tag.Value;
            }

            lock (_measurements)
            {
                _measurements.Add(new Measurement(name, value, copied));
            }
        }

        public List<Measurement> Measurements(string instrument)
        {
            lock (_measurements)
            {
                return _measurements.Where(m => m.Instrument == instrument).ToList();
            }
        }

        public void Dispose() => _listener.Dispose();
    }

    private sealed record Measurement(
        string Instrument,
        double Value,
        IReadOnlyDictionary<string, object?> Tags)
    {
        public object? Tag(string key) => Tags.TryGetValue(key, out var value) ? value : null;
    }
}
