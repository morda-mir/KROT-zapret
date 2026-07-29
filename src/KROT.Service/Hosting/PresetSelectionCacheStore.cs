using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using KROT.Zapret.Profiles;
using Newtonsoft.Json;

namespace KROT.Service.Hosting;

public sealed class PresetSelectionCacheStore : IPresetSelectionCache
{
    private const long MaxCacheBytes = 1024 * 1024;
    private readonly string _path;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public PresetSelectionCacheStore(string path)
    {
        _path = path;
    }

    public async Task<PresetSelection?> LoadAsync(
        string networkFingerprint,
        CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var document = await ReadAsync(cancellationToken).ConfigureAwait(false);
            document.Entries ??= new List<PresetCacheEntry>();
            var entry = document.Entries
                .OrderByDescending(item => item.UpdatedUtc)
                .FirstOrDefault(item => string.Equals(
                    item.NetworkFingerprintSha256,
                    networkFingerprint,
                    StringComparison.Ordinal));
            return entry?.Selection != null && IsValid(entry.Selection)
                ? entry.Selection.Clone()
                : null;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SaveAsync(
        string networkFingerprint,
        PresetSelection selection,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(networkFingerprint) || !IsValid(selection))
        {
            return;
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var document = await ReadAsync(cancellationToken).ConfigureAwait(false);
            document.Entries ??= new List<PresetCacheEntry>();
            document.Entries.RemoveAll(item => string.Equals(
                item.NetworkFingerprintSha256,
                networkFingerprint,
                StringComparison.Ordinal));
            document.Entries.Add(new PresetCacheEntry
            {
                NetworkFingerprintSha256 = networkFingerprint,
                Selection = selection.Clone(),
                UpdatedUtc = DateTime.UtcNow
            });
            document.Entries = document.Entries
                .OrderByDescending(item => item.UpdatedUtc)
                .Take(20)
                .ToList();
            await WriteAsync(document, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<PresetCacheDocument> ReadAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_path))
        {
            return new PresetCacheDocument();
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
            if (stream.Length > MaxCacheBytes)
            {
                throw new InvalidDataException(
                    "Preset cache exceeds the safe size limit.");
            }

            using var reader = new StreamReader(stream, Encoding.UTF8, true);
            var json = await reader.ReadToEndAsync().ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            return JsonConvert.DeserializeObject<PresetCacheDocument>(json)
                   ?? new PresetCacheDocument();
        }
        catch (JsonException)
        {
            return new PresetCacheDocument();
        }
        catch (IOException)
        {
            return new PresetCacheDocument();
        }
        catch (InvalidDataException)
        {
            return new PresetCacheDocument();
        }
    }

    private async Task WriteAsync(
        PresetCacheDocument document,
        CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(_path)
                        ?? throw new InvalidOperationException("Preset cache path has no directory.");
        Directory.CreateDirectory(directory);
        var temporaryPath = _path + ".tmp";
        var json = JsonConvert.SerializeObject(document, Formatting.Indented);

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
            File.Replace(temporaryPath, _path, destinationBackupFileName: null);
        }
        else
        {
            File.Move(temporaryPath, _path);
        }
    }

    private static bool IsValid(PresetSelection selection) =>
        BuiltInStrategyCatalog.IsTcp(selection.DiscordTcp)
        && BuiltInStrategyCatalog.IsTcp(selection.YouTubeTcp)
        && BuiltInStrategyCatalog.IsQuic(selection.YouTubeQuic)
        && BuiltInStrategyCatalog.IsVoice(selection.DiscordVoice);

    private sealed class PresetCacheDocument
    {
        public int SchemaVersion { get; set; } = 1;

        public List<PresetCacheEntry>? Entries { get; set; } = new();
    }

    private sealed class PresetCacheEntry
    {
        public string NetworkFingerprintSha256 { get; set; } = string.Empty;

        public PresetSelection Selection { get; set; } = new();

        public DateTime UpdatedUtc { get; set; }
    }
}
