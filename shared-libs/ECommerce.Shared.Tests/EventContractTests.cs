using System.Text.Json;
using ECommerce.Shared.Infrastructure.EventBus;
using ECommerce.Shared.IntegrationEvents.Commands;

namespace ECommerce.Shared.Tests;

public class EventContractTests
{
    [Fact]
    public void Given_Event_When_SagaMetadataIsAssigned_Then_MetadataIsCarried()
    {
        var causationId = Guid.NewGuid();
        var sagaId = Guid.NewGuid();

        var @event = new Event
        {
            CausationId = causationId,
            SagaId = sagaId
        };

        Assert.Equal(causationId, @event.CausationId);
        Assert.Equal(sagaId, @event.SagaId);
    }

    [Fact]
    public void Given_Command_When_Constructed_Then_SagaMetadataIsRequired()
    {
        var causationId = Guid.NewGuid();
        var sagaId = Guid.NewGuid();

        var command = new TestCommand(causationId, sagaId);

        Assert.Equal(causationId, command.CausationId);
        Assert.Equal(sagaId, command.SagaId);
        Assert.Throws<ArgumentException>(() => new TestCommand(Guid.Empty, sagaId));
        Assert.Throws<ArgumentException>(() => new TestCommand(causationId, Guid.Empty));
    }

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

    private sealed record TestCommand : Command
    {
        public TestCommand(Guid causationId, Guid sagaId)
            : base(causationId, sagaId)
        {
        }
    }
}
