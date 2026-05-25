using System.Text.Json;
using ECommerce.Shared.IntegrationEvents.Commands;

namespace ECommerce.Shared.Contracts.Tests;

public class ReserveStockCommandSerializationTests
{
    [Fact]
    public void Given_ReserveStockCommand_When_Serialized_Then_CanDeserialize()
    {
        var command = new ReserveStockCommand(
            Guid.NewGuid(),
            "customer-1",
            [new ReserveStockItem("101", 2, 12.50m)],
            "USD",
            Guid.NewGuid(),
            Guid.NewGuid())
        {
            CorrelationId = Guid.NewGuid()
        };

        var json = JsonSerializer.Serialize(command, command.GetType());

        var deserialized = JsonSerializer.Deserialize<ReserveStockCommand>(json);

        Assert.NotNull(deserialized);
        Assert.Equal(command.OrderId, deserialized.OrderId);
        Assert.Equal(command.CausationId, deserialized.CausationId);
        Assert.Equal(command.SagaId, deserialized.SagaId);
    }
}
