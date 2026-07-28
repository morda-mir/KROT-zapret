using System;
using System.IO;
using System.Linq;
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

    [Fact]
    public async Task Load_ReplacesDefaultsAndRemovesDuplicateServices()
    {
        var path = Path.Combine(_directory, "settings.json");
        Directory.CreateDirectory(_directory);
        File.WriteAllText(
            path,
            "{\"Services\":[{\"Id\":0,\"IsEnabled\":true},{\"Id\":0,\"IsEnabled\":false},{\"Id\":2,\"IsEnabled\":true},{\"Id\":3,\"IsEnabled\":true}]}");

        var loaded = await new AtomicJsonSettingsStore(path)
            .LoadAsync(CancellationToken.None);

        Assert.Equal(2, loaded.Services.Count);
        Assert.Equal(KrotSettings.CurrentSchemaVersion, loaded.SchemaVersion);
        Assert.True(loaded.Services.Single(x => x.Id == ServiceId.Discord).IsEnabled);
        Assert.DoesNotContain(loaded.Services, x => (int)x.Id == 2);
        Assert.DoesNotContain("\"Id\": 2", File.ReadAllText(path));
        Assert.DoesNotContain("\"Id\": 3", File.ReadAllText(path));
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }
}
