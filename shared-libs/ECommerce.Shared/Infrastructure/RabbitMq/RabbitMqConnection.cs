using RabbitMQ.Client;

namespace ECommerce.Shared.Infrastructure.RabbitMq;

public class RabbitMqConnection : IDisposable, IRabbitMqConnection
{
    private readonly Lazy<IConnection> _connection;

    public IConnection Connection => _connection.Value;

    public RabbitMqConnection(RabbitMqOptions options)
    {
        _connection = new Lazy<IConnection>(
            () => new ConnectionFactory { HostName = options.HostName }.CreateConnection(),
            LazyThreadSafetyMode.ExecutionAndPublication);
    }

    public void Dispose()
    {
        if (_connection.IsValueCreated)
        {
            _connection.Value.Dispose();
        }
        GC.SuppressFinalize(this);
    }
}
