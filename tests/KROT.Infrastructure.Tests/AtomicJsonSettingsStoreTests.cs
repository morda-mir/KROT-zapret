using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using KROT.Core.Models;
using KROT.Infrastructure.Storage;
using Xunit;

namespace KROT.Infrastructure.Tests;

public sealed class AtomicJsonSettingsStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "KROT-tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task SaveAndLoad_RoundTripsSettingsAndCreatesBackup()
    {
        var path = Path.Combine(_directory, "settings.json");
        var store = new AtomicJsonSettingsStore(path);
        var settings = new KrotSettings { Language = "en", DetailedLogs = true };

        await store.SaveAsync(settings, CancellationToken.None);
        settings.Language = "ru";
        await store.SaveAsync(settings, CancellationToken.None);
        var loaded = await store.LoadAsync(CancellationToken.None);

        Assert.Equal("ru", loaded.Language);
        Assert.True(loaded.DetailedLogs);
        Assert.True(File.Exists(path + ".bak"));
        Assert.False(File.Exists(path + ".tmp"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }
}

