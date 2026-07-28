using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using KROT.Core.Contracts;
using KROT.Core.Models;
using Newtonsoft.Json;

namespace KROT.Infrastructure.Storage;

public sealed class AtomicJsonSettingsStore : ISettingsStore
{
    private readonly string _path;
    private readonly SemaphoreSlim _saveGate = new(1, 1);
    private readonly JsonSerializerSettings _serializerSettings = new()
    {
        Formatting = Formatting.Indented,
        MissingMemberHandling = MissingMemberHandling.Ignore,
        ObjectCreationHandling = ObjectCreationHandling.Replace
    };

    public AtomicJsonSettingsStore(string? path = null)
    {
        _path = path ?? AppPaths.SettingsFile;
    }

    public async Task<KrotSettings> LoadAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_path))
        {
            return new KrotSettings();
        }

        try
        {
            var json = await ReadAllTextAsync(_path, cancellationToken).ConfigureAwait(false);
            var loaded = JsonConvert.DeserializeObject<KrotSettings>(json, _serializerSettings)
                ?? new KrotSettings();
            return await NormalizeAndPersistAsync(loaded, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (JsonException)
        {
            var backup = _path + ".bak";
            if (!File.Exists(backup))
            {
                return new KrotSettings();
            }

            var json = await ReadAllTextAsync(backup, cancellationToken).ConfigureAwait(false);
            var loaded = JsonConvert.DeserializeObject<KrotSettings>(json, _serializerSettings)
                ?? new KrotSettings();
            return await NormalizeAndPersistAsync(loaded, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    public async Task SaveAsync(KrotSettings settings, CancellationToken cancellationToken)
    {
        await _saveGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var directory = Path.GetDirectoryName(_path)
                ?? throw new InvalidOperationException("Settings path has no directory.");
            Directory.CreateDirectory(directory);

            var temporaryPath = _path + ".tmp";
            var backupPath = _path + ".bak";
            var json = JsonConvert.SerializeObject(settings, _serializerSettings);

            using (var stream = new FileStream(
                       temporaryPath,
                       FileMode.Create,
                       FileAccess.Write,
                       FileShare.None,
                       4096,
                       useAsync: true))
            using (var writer = new StreamWriter(stream, new UTF8Encoding(false)))
            {
                await writer.WriteAsync(json).ConfigureAwait(false);
                await writer.FlushAsync().ConfigureAwait(false);
                stream.Flush(flushToDisk: true);
            }

            cancellationToken.ThrowIfCancellationRequested();

            if (File.Exists(_path))
            {
                File.Replace(temporaryPath, _path, backupPath, ignoreMetadataErrors: true);
            }
            else
            {
                File.Move(temporaryPath, _path);
            }
        }
        finally
        {
            _saveGate.Release();
        }
    }

    private static async Task<string> ReadAllTextAsync(string path, CancellationToken cancellationToken)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, true);
        using var reader = new StreamReader(stream, Encoding.UTF8, true);
        var value = await reader.ReadToEndAsync().ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        return value;
    }

    private static KrotSettings Normalize(KrotSettings settings)
    {
        settings.SchemaVersion = KrotSettings.CurrentSchemaVersion;
        settings.Services = settings.Services
            .Where(x => Enum.IsDefined(typeof(ServiceId), x.Id))
            .GroupBy(x => x.Id)
            .Select(x => x.First())
            .ToList();

        foreach (ServiceId serviceId in Enum.GetValues(typeof(ServiceId)))
        {
            if (settings.Services.All(x => x.Id != serviceId))
            {
                settings.Services.Add(new ServiceSelection
                {
                    Id = serviceId,
                    IsEnabled = false
                });
            }
        }

        return settings;
    }

    private async Task<KrotSettings> NormalizeAndPersistAsync(
        KrotSettings settings,
        CancellationToken cancellationToken)
    {
        var requiresRewrite =
            settings.SchemaVersion != KrotSettings.CurrentSchemaVersion
            || settings.Services.Count != Enum.GetValues(typeof(ServiceId)).Length
            || settings.Services.Any(item => !Enum.IsDefined(typeof(ServiceId), item.Id))
            || settings.Services.GroupBy(item => item.Id).Any(group => group.Count() > 1);
        var normalized = Normalize(settings);
        if (requiresRewrite)
        {
            await SaveAsync(normalized, cancellationToken).ConfigureAwait(false);
        }

        return normalized;
    }
}
