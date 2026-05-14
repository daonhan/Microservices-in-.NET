namespace ECommerce.Shared.Infrastructure.DeadLetter;

internal sealed class AzureServiceBusDeadLetterCapture : IDeadLetterCapture
{
    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
