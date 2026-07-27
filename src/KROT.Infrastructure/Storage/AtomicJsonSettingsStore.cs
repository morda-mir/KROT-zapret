using System;
using System.IO;
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
        MissingMemberHandling = MissingMemberHandling.Ignore
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
            using var stream = new FileStream(
                _path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                4096,
                useAsync: true);
            using var reader = new StreamReader(stream, Encoding.UTF8, true);
            var json = await reader.ReadToEndAsync().ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            return JsonConvert.DeserializeObject<KrotSettings>(json, _serializerSettings) ?? new KrotSettings();
        }
        catch (JsonException)
        {
            var backup = _path + ".bak";
            if (!File.Exists(backup))
            {
                return new KrotSettings();
            }

            var json = await ReadAllTextAsync(backup, cancellationToken).ConfigureAwait(false);
            return JsonConvert.DeserializeObject<KrotSettings>(json, _serializerSettings) ?? new KrotSettings();
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
}
