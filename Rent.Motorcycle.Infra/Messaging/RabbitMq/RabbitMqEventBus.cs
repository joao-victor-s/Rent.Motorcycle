using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;

namespace Rent.Motorcycle.Infra.Messaging.RabbitMq;
public sealed class RabbitMqEventBus : IEventBus, IDisposable
{
    private readonly IConnection _conn;
    private readonly IModel _ch;
    private readonly RabbitMqOptions _opt;

    public RabbitMqEventBus(IOptions<RabbitMqOptions> opt)
    {
        _opt = opt.Value;
        var factory = new ConnectionFactory
        {
            HostName = _opt.Host,
            Port = _opt.Port,
            VirtualHost = _opt.VirtualHost,
            UserName = _opt.User,
            Password = _opt.Password,
            DispatchConsumersAsync = true
        };
        _conn = factory.CreateConnection();
        _ch = _conn.CreateModel();
        _ch.ExchangeDeclare(_opt.Exchange, ExchangeType.Topic, durable: true, autoDelete: false);
    }

    public Task PublishAsync<T>(string routingKey, T message, CancellationToken ct = default)
    {
        var body = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(message));
        var props = _ch.CreateBasicProperties();
        props.ContentType = "application/json";
        props.DeliveryMode = 2;
        _ch.BasicPublish(_opt.Exchange, routingKey, props, body);
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        _ch?.Dispose();
        _conn?.Dispose();
    }
}
