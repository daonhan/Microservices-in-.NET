using Microsoft.Extensions.Options;
using Saga.Service.Domain;
using Saga.Service.Domain.OrderSaga;

namespace Saga.Service.Infrastructure.Reaper;

internal sealed class OrderSagaTimeoutScheduler
{
    private readonly OrderSagaTimeoutOptions _options;

    public OrderSagaTimeoutScheduler(IOptions<OrderSagaTimeoutOptions> options)
    {
        _options = options.Value;
    }

    public TimeSpan? GetTimeout(OrderSagaStep step) =>
        step switch
        {
            OrderSagaStep.StockReserving => _options.StockReservingTimeout,
            OrderSagaStep.PaymentAuthorizing => _options.PaymentAuthorizingTimeout,
            OrderSagaStep.OrderConfirming => _options.OrderConfirmingTimeout,
            OrderSagaStep.StockCommitting => _options.StockCommittingTimeout,
            OrderSagaStep.ShipmentCreating => _options.ShipmentCreatingTimeout,
            _ => null
        };

    public void Apply(SagaInstance saga, DateTime now)
    {
        if (saga.Status != SagaStatus.Running
            || !Enum.TryParse<OrderSagaStep>(saga.CurrentStep, out var step)
            || GetTimeout(step) is not { } timeout)
        {
            saga.NextTimeoutAt = null;
            saga.RetryCount = 0;
            return;
        }

        saga.NextTimeoutAt = now.Add(timeout);
        saga.RetryCount = 0;
    }

    public void MoveForward(SagaInstance saga, DateTime now)
    {
        if (!Enum.TryParse<OrderSagaStep>(saga.CurrentStep, out var step)
            || GetTimeout(step) is not { } timeout)
        {
            saga.NextTimeoutAt = null;
            return;
        }

        saga.NextTimeoutAt = now.Add(timeout);
    }
}
