using ECommerce.Shared.Infrastructure.EventBus;

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

    private sealed record TestCommand : Command
    {
        public TestCommand(Guid causationId, Guid sagaId)
            : base(causationId, sagaId)
        {
        }
    }
}
