using System.Threading;
using System.Threading.Tasks;

namespace Rent.Motorcycle.Infra.Messaging;

public interface IEventBus
{
    Task PublishAsync<T>(string routingKey, T message, CancellationToken ct = default);
}
