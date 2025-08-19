using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Rent.Motorcycle.Infra.Storage
{
    public sealed class DiskStorageService : IStorageService
    {
        private readonly string _root;

        public DiskStorageService()
        {
            _root = Environment.GetEnvironmentVariable("STORAGE_ROOT") ?? Path.Combine(AppContext.BaseDirectory, "storage");
            Directory.CreateDirectory(_root);
        }

        public async Task<string> SaveAsync(Stream content, string fileName, CancellationToken ct)
        {
            var safeName = $"{Guid.NewGuid():N}_{Path.GetFileName(fileName)}";
            var path = Path.Combine(_root, safeName);
            await using var fs = File.Create(path);
            await content.CopyToAsync(fs, ct);
            return path;
        }
    }
}
