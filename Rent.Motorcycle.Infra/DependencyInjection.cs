using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Rent.Motorcycle.Infra.Data;
using Rent.Motorcycle.Infra.Storage;
using Rent.Motorcycle.Infra.Messaging;
using Rent.Motorcycle.Infra.Messaging.RabbitMq;

namespace Rent.Motorcycle.Infra;

public static class DependencyInjection
{
    public static IServiceCollection AddInfra(this IServiceCollection services, IConfiguration cfg)
    {
        services.AddDbContext<RentDbContext>(opt =>
        {
            var cs = cfg.GetConnectionString("Postgres")
                    ?? cfg.GetConnectionString("Default")
                    ?? throw new InvalidOperationException("Missing connection string 'Postgres' or 'Default'.");
            opt.UseNpgsql(cs, npg => npg.EnableRetryOnFailure());
        });

        services.Configure<RabbitMqOptions>(cfg.GetSection("RabbitMq"));
        services.AddSingleton<IEventBus, RabbitMqEventBus>();
        services.AddHostedService<MotorcycleRegisteredConsumerService>();

        services.AddSingleton<IStorageService, DiskStorageService>();
        return services;
    }
}
