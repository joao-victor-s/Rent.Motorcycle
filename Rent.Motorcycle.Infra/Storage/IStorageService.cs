using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Rent.Motorcycle.Infra.Storage
{
    public interface IStorageService
    {
        Task<string> SaveAsync(Stream content, string fileName, CancellationToken ct);
    }
}
