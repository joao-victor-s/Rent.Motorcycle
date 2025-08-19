namespace Rent.Motorcycle.Infra.Messaging.RabbitMq;

public sealed class RabbitMqOptions
{
    public string Host { get; init; } = default!;
    public int Port { get; init; } = 5672;
    public string VirtualHost { get; init; } = "/";
    public string User { get; init; } = default!;
    public string Password { get; init; } = default!;
    public string Exchange { get; init; } = "rent.events";
}
