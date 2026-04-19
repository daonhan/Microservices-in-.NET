using System.Text;
using System.Text.Json;
using ECommerce.Shared.Infrastructure.EventBus;
using ECommerce.Shared.Infrastructure.RabbitMq;
using Inventory.Service.Infrastructure.Data.EntityFramework;
using Microsoft.Extensions.DependencyInjection;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace Inventory.Tests;

public class IntegrationTestBase : IClassFixture<InventoryWebApplicationFactory>, IDisposable
{
    private const string QueueName = "inventory-integration-tests";
    private const string ExchangeName = "ecommerce-exchange";

    private IModel? _model;

    internal readonly InventoryContext InventoryContext;
    internal readonly HttpClient HttpClient;
    internal readonly IRabbitMqConnection RabbitMqConnection;
    internal List<Event> ReceivedEvents = [];

    public IntegrationTestBase(InventoryWebApplicationFactory webApplicationFactory)
    {
        var scope = webApplicationFactory.Services.CreateScope();
        InventoryContext = scope.ServiceProvider.GetRequiredService<InventoryContext>();
        HttpClient = webApplicationFactory.CreateClient();
        RabbitMqConnection = scope.ServiceProvider.GetRequiredService<IRabbitMqConnection>();
    }

    public void Subscribe<TEvent>() where TEvent : Event
    {
        _model = RabbitMqConnection.Connection.CreateModel();

        _model.ExchangeDeclare(ExchangeName, "fanout", durable: false, autoDelete: false, null);
        _model.QueueDeclare(QueueName, durable: false, exclusive: false, autoDelete: false, null);

        EventingBasicConsumer eventingBasicConsumer = new(_model);

        eventingBasicConsumer.Received += (sender, eventArgs) =>
        {
            var body = Encoding.UTF8.GetString(eventArgs.Body.Span);
            var @event = JsonSerializer.Deserialize<TEvent>(body);

            if (@event is not null)
            {
                ReceivedEvents.Add(@event);
            }
        };

        _model.BasicConsume(QueueName, true, eventingBasicConsumer);
        _model.QueueBind(QueueName, ExchangeName, typeof(TEvent).Name);
    }

    public void Dispose()
    {
        if (_model is not null)
        {
            _model.QueueDelete(QueueName);
            _model.ExchangeDelete(ExchangeName);
        }

        GC.SuppressFinalize(this);
    }
}
