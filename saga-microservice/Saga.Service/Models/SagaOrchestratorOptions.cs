namespace Saga.Service.Models;

internal sealed class SagaOrchestratorOptions
{
    public bool Enabled { get; set; }

    public Guid[] AllowList { get; set; } = [];

    public int Percentage { get; set; }
}
