using ECommerce.Shared.Infrastructure.EventBus;
using ECommerce.Shared.Infrastructure.EventBus.Abstractions;
using ECommerce.Shared.Infrastructure.RabbitMq;
using Microsoft.Extensions.Options;
using NSubstitute;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using Testcontainers.RabbitMq;

namespace ECommerce.Shared.Tests;

public sealed class RabbitMqPublisherTests
{
    [Fact]
    public async Task Given_publish_When_confirmed_Then_message_is_persistent_and_identifiable()
    {
        var channel = Substitute.For<IModel>();
        var properties = Substitute.For<IBasicProperties>();
        channel.CreateBasicProperties().Returns(properties);
        var @event = new TestEvent();
        await CreateBus(channel).PublishAsync(@event);
        Received.InOrder(() =>
        {
            channel.ConfirmSelect();
            channel.BasicPublish(RabbitMqTopology.ExchangeName, nameof(TestEvent), true, properties, Arg.Any<ReadOnlyMemory<byte>>());
            channel.WaitForConfirmsOrDie(TimeSpan.FromSeconds(10));
        });
        Assert.True(properties.Persistent);
        Assert.Equal(@event.Id.ToString(), properties.MessageId);
        Assert.Equal("application/json", properties.ContentType);
        channel.Received().ExchangeDeclare(RabbitMqTopology.ExchangeName, "fanout", false, false, null);
    }

    [Fact]
    public async Task Given_broker_nack_or_confirm_timeout_When_publishing_Then_failure_reaches_outbox()
    {
        var channel = Substitute.For<IModel>();
        channel.CreateBasicProperties().Returns(Substitute.For<IBasicProperties>());
        channel.When(c => c.WaitForConfirmsOrDie(Arg.Any<TimeSpan>()))
            .Do(_ => throw new IOException("nack or confirm timeout"));
        await Assert.ThrowsAsync<IOException>(() => CreateBus(channel).PublishAsync(new TestEvent()));
    }

    [Fact]
    public async Task Given_return_before_confirm_When_publishing_Then_ack_does_not_hide_unroutable_message()
    {
        var channel = Substitute.For<IModel>();
        channel.CreateBasicProperties().Returns(Substitute.For<IBasicProperties>());
        channel.When(c => c.WaitForConfirmsOrDie(Arg.Any<TimeSpan>())).Do(_ =>
            channel.BasicReturn += Raise.Event<EventHandler<BasicReturnEventArgs>>(channel, new BasicReturnEventArgs()));
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => CreateBus(channel).PublishAsync(new TestEvent()));
        Assert.Contains("unroutable", error.Message, StringComparison.Ordinal);
    }

    private static RabbitMqEventBus CreateBus(IModel channel)
    {
        var connection = Substitute.For<IConnection>();
        connection.CreateModel().Returns(channel);
        var wrapper = Substitute.For<IRabbitMqConnection>();
        wrapper.Connection.Returns(connection);
        return new RabbitMqEventBus(wrapper, new RabbitMqTelemetry(), Options.Create(new EventBusOptions { RetryCount = 1 }));
    }

    private sealed record TestEvent : Event;
}

[Trait("Category", "Integration")]
public sealed class RabbitMqPublisherIntegrationTests : IAsyncLifetime
{
    private readonly RabbitMqContainer _rabbit = new RabbitMqBuilder("rabbitmq:3.13-management-alpine").Build();
    private IConnection _connection = null!;

    public async Task InitializeAsync()
    {
        await _rabbit.StartAsync();
        _connection = new ConnectionFactory { Uri = new Uri(_rabbit.GetConnectionString()) }.CreateConnection();
    }

    public async Task DisposeAsync()
    {
        _connection.Dispose();
        await _rabbit.DisposeAsync();
    }

    [Fact]
    public async Task Given_existing_legacy_topology_When_publisher_confirms_Then_durable_queue_receives_persistent_message()
    {
        using var channel = _connection.CreateModel();
        RabbitMqHostedService.DeclareTopology(channel, "publisher-tests");
        channel.QueueBind("publisher-tests", RabbitMqTopology.ExchangeName, string.Empty);
        var wrapper = Substitute.For<IRabbitMqConnection>();
        wrapper.Connection.Returns(_connection);
        var bus = new RabbitMqEventBus(wrapper, new RabbitMqTelemetry(), Options.Create(new EventBusOptions()));
        var @event = new TestEvent();
        await bus.PublishAsync(@event);
        var delivered = channel.BasicGet("publisher-tests", true);
        Assert.NotNull(delivered);
        Assert.True(delivered.BasicProperties.Persistent);
        Assert.Equal(@event.Id.ToString(), delivered.BasicProperties.MessageId);
    }

    [Fact]
    public async Task Given_no_bound_queue_When_broker_confirms_Then_unroutable_publish_fails()
    {
        var wrapper = Substitute.For<IRabbitMqConnection>();
        wrapper.Connection.Returns(_connection);
        var bus = new RabbitMqEventBus(wrapper, new RabbitMqTelemetry(), Options.Create(new EventBusOptions()));
        await Assert.ThrowsAsync<InvalidOperationException>(() => bus.PublishAsync(new TestEvent()));
    }

    private sealed record TestEvent : Event;
}
