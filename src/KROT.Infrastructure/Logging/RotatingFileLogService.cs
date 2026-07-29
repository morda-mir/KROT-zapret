using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security;
using System.Text;
using System.Threading;
using KROT.Core.Contracts;
using KROT.Infrastructure.Storage;

namespace KROT.Infrastructure.Logging;

public sealed class RotatingFileLogService : IConfigurableLogService
{
    private const int MaxEventNameChars = 256;
    private const int MaxMessageChars = 16 * 1024;
    private const int MaxExceptionChars = 32 * 1024;
    private const long MinimumFileBytes = 512;
    private static readonly UTF8Encoding Utf8 = new(false);
    private readonly object _sync = new();
    private readonly string _directory;
    private readonly long _maxFileBytes;
    private readonly int _maxFiles;
    private bool _detailed;

    public RotatingFileLogService(
        bool detailed,
        string? directory = null,
        long maxFileBytes = 2 * 1024 * 1024,
        int maxFiles = 5)
    {
        if (maxFileBytes < MinimumFileBytes)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxFileBytes),
                $"Log files must be at least {MinimumFileBytes} bytes.");
        }

        if (maxFiles < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(maxFiles));
        }

        _detailed = detailed;
        _directory = directory ?? AppPaths.LogsDirectory;
        _maxFileBytes = maxFileBytes;
        _maxFiles = maxFiles;
    }

    public bool Detailed
    {
        get => Volatile.Read(ref _detailed);
        set => Volatile.Write(ref _detailed, value);
    }

    public void Info(string eventName, string message) =>
        Write("INFO", eventName, message, null);

    public void Detail(string eventName, string message)
    {
        if (Detailed)
        {
            Write("DETAIL", eventName, message, null);
        }
    }

    public void Error(string eventName, string message, Exception? exception = null) =>
        Write("ERROR", eventName, message, exception);

    private void Write(string level, string eventName, string message, Exception? exception)
    {
        try
        {
            lock (_sync)
            {
                Directory.CreateDirectory(_directory);
                var line = string.Format(
                    CultureInfo.InvariantCulture,
                    "{0:O}\t{1}\t{2}\t{3}",
                    DateTime.UtcNow,
                    level,
                    SanitizeAndLimit(eventName, MaxEventNameChars),
                    SanitizeAndLimit(message, MaxMessageChars));

                if (exception != null)
                {
                    line += "\t" + SanitizeAndLimit(exception.ToString(), MaxExceptionChars);
                }

                var entry = EncodeBounded(line);
                RotateIfNeeded(entry.Length);
                using var stream = new FileStream(
                    CurrentLogPath(),
                    FileMode.Append,
                    FileAccess.Write,
                    FileShare.Read);
                stream.Write(entry, 0, entry.Length);
            }
        }
        catch (Exception ex) when (
            ex is IOException
            or UnauthorizedAccessException
            or SecurityException
            or NotSupportedException
            or ArgumentException)
        {
            // Logging must never prevent the GUI or runtime from stopping.
        }
    }

    private byte[] EncodeBounded(string line)
    {
        var fullEntry = line + Environment.NewLine;
        if (Utf8.GetByteCount(fullEntry) <= _maxFileBytes)
        {
            return Utf8.GetBytes(fullEntry);
        }

        const string suffixText = "\t...[entry truncated]";
        var suffix = suffixText + Environment.NewLine;
        var maximumBytes = checked((int)Math.Min(_maxFileBytes, int.MaxValue));
        var low = 0;
        var high = line.Length;
        while (low < high)
        {
            var candidateLength = low + ((high - low + 1) / 2);
            var byteCount = Utf8.GetByteCount(
                line.Substring(0, candidateLength) + suffix);
            if (byteCount <= maximumBytes)
            {
                low = candidateLength;
            }
            else
            {
                high = candidateLength - 1;
            }
        }

        return Utf8.GetBytes(line.Substring(0, low) + suffix);
    }

    private void RotateIfNeeded(int incomingBytes)
    {
        var current = CurrentLogPath();
        if (File.Exists(current))
        {
            var currentLength = new FileInfo(current).Length;
            if (currentLength > 0
                && (currentLength >= _maxFileBytes
                    || incomingBytes > _maxFileBytes - currentLength))
            {
                var archive = Path.Combine(
                    _directory,
                    $"krot-{DateTime.UtcNow:yyyyMMdd-HHmmssfff}-{Guid.NewGuid():N}.log");
                File.Move(current, archive);
            }
        }

        var oldFiles = Directory.GetFiles(_directory, "krot-*.log")
            .Where(path => !string.Equals(
                Path.GetFileName(path),
                "krot-current.log",
                StringComparison.OrdinalIgnoreCase))
            .Select(path => new FileInfo(path))
            .OrderByDescending(file => file.LastWriteTimeUtc)
            .Skip(_maxFiles - 1)
            .ToList();

        foreach (var oldFile in oldFiles)
        {
            oldFile.Delete();
        }
    }

    private string CurrentLogPath() => Path.Combine(_directory, "krot-current.log");

    private static string SanitizeAndLimit(string value, int maxChars)
    {
        var sanitized = (value ?? string.Empty)
            .Replace("\r", " ")
            .Replace("\n", " ")
            .Replace("\t", " ");
        return sanitized.Length <= maxChars
            ? sanitized
            : sanitized.Substring(0, maxChars) + "...[truncated]";
    }
}
