using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using KROT.Service.Hosting;
using Xunit;

namespace KROT.Service.Tests;

public sealed class PresetSelectionCacheStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "KROT-cache-tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Load_IgnoresOversizedCacheWithoutReadingIt()
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, "preset-cache.json");
        File.WriteAllText(path, new string('x', (1024 * 1024) + 1));
        var store = new PresetSelectionCacheStore(path);

        var selection = await store.LoadAsync(
            new string('a', 64),
            CancellationToken.None);

        Assert.Null(selection);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }
}
