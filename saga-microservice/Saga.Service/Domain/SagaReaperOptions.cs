namespace Saga.Service.Domain;

internal sealed class SagaReaperOptions
{
    public int IntervalInSeconds { get; set; } = 30;

    public int MaxRetries { get; set; } = 3;
}
