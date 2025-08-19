using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;

public class MotorcycleRegisteredConsumerService : BackgroundService
{
    private readonly ILogger<MotorcycleRegisteredConsumerService> _logger;
    private readonly ConnectionFactory _factory;

    public MotorcycleRegisteredConsumerService(IConfiguration cfg, ILogger<MotorcycleRegisteredConsumerService> logger)
    {
        _logger = logger;

        var s = cfg.GetSection("RabbitMq"); // <<-- seção padrão do projeto

        // Validações explícitas para não cair com NullException
        var host  = s["Host"]        ?? throw new InvalidOperationException("RabbitMq:Host ausente");
        var user  = s["User"]        ?? throw new InvalidOperationException("RabbitMq:User ausente");
        var pass  = s["Password"]    ?? throw new InvalidOperationException("RabbitMq:Password ausente");
        var vhost = s["VirtualHost"] ?? "/";

        var port  = int.TryParse(s["Port"], out var p) ? p : 5672;

        _factory = new ConnectionFactory
        {
            HostName = host,
            Port = port,
            VirtualHost = vhost,
            UserName = user,       // usa RabbitMq:User
            Password = pass,       // usa RabbitMq:Password
            DispatchConsumersAsync = true,
            AutomaticRecoveryEnabled = true,
            NetworkRecoveryInterval = TimeSpan.FromSeconds(5),
            RequestedHeartbeat = TimeSpan.FromSeconds(30)
        };
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var conn = _factory.CreateConnection("rent-api");
                using var ch = conn.CreateModel();


                _logger.LogInformation("RabbitMQ consumidor iniciado.");
                await Task.Delay(Timeout.Infinite, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Falha ao conectar no RabbitMQ. Nova tentativa em 5s…");
                try { await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken); } catch { }
            }
        }
    }
}
