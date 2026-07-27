using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using KROT.Core.Contracts;
using KROT.Infrastructure.Storage;

namespace KROT.Infrastructure.Logging;

public sealed class RotatingFileLogService : ILogService
{
    private readonly object _sync = new();
    private readonly string _directory;
    private readonly long _maxFileBytes;
    private readonly int _maxFiles;
    private readonly bool _detailed;

    public RotatingFileLogService(
        bool detailed,
        string? directory = null,
        long maxFileBytes = 2 * 1024 * 1024,
        int maxFiles = 5)
    {
        _detailed = detailed;
        _directory = directory ?? AppPaths.LogsDirectory;
        _maxFileBytes = maxFileBytes;
        _maxFiles = maxFiles;
    }

    public void Info(string eventName, string message)
    {
        if (_detailed)
        {
            Write("INFO", eventName, message, null);
        }
    }

    public void Error(string eventName, string message, Exception? exception = null) =>
        Write("ERROR", eventName, message, exception);

    private void Write(string level, string eventName, string message, Exception? exception)
    {
        lock (_sync)
        {
            Directory.CreateDirectory(_directory);
            RotateIfNeeded();
            var safeMessage = Sanitize(message);
            var line = string.Format(
                CultureInfo.InvariantCulture,
                "{0:O}\t{1}\t{2}\t{3}",
                DateTime.UtcNow,
                level,
                Sanitize(eventName),
                safeMessage);

            if (exception != null)
            {
                line += Environment.NewLine + Sanitize(exception.ToString());
            }

            File.AppendAllText(CurrentLogPath(), line + Environment.NewLine, new UTF8Encoding(false));
        }
    }

    private void RotateIfNeeded()
    {
        var current = CurrentLogPath();
        if (File.Exists(current) && new FileInfo(current).Length >= _maxFileBytes)
        {
            var archive = Path.Combine(
                _directory,
                $"krot-{DateTime.UtcNow:yyyyMMdd-HHmmssfff}-{Guid.NewGuid():N}.log");
            File.Move(current, archive);
        }

        var oldFiles = Directory.GetFiles(_directory, "krot-*.log")
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

    private static string Sanitize(string value) =>
        value.Replace("\r", " ").Replace("\n", " ").Replace("\t", " ");
}
