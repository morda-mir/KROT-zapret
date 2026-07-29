using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using KROT.Infrastructure.Logging;
using Xunit;

namespace KROT.Infrastructure.Tests;

public sealed class RotatingFileLogServiceTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "KROT-log-tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public void Rotation_KeepsBoundedNumberOfFiles()
    {
        const int maxFileBytes = 1024;
        var log = new RotatingFileLogService(
            true,
            _directory,
            maxFileBytes,
            maxFiles: 3);

        for (var index = 0; index < 40; index++)
        {
            log.Info("test.event", new string('x', 300));
        }

        var files = Directory.GetFiles(_directory, "krot-*.log");
        Assert.InRange(files.Length, 1, 3);
        Assert.All(files, file => Assert.InRange(new FileInfo(file).Length, 1, maxFileBytes));
    }

    [Fact]
    public void Detailed_CanBeChangedAtRuntime()
    {
        var log = new RotatingFileLogService(false, _directory);

        log.Info("baseline", "always written");
        log.Detail("hidden", "not written");
        log.Detailed = true;
        log.Detail("visible", "written");

        var content = File.ReadAllText(Path.Combine(_directory, "krot-current.log"));
        Assert.Contains("baseline", content);
        Assert.DoesNotContain("hidden", content);
        Assert.Contains("visible", content);
    }

    [Fact]
    public void OversizedEntry_IsTruncatedToFileLimit()
    {
        const int maxFileBytes = 512;
        var log = new RotatingFileLogService(
            true,
            _directory,
            maxFileBytes,
            maxFiles: 2);

        log.Error(
            "oversized",
            new string('x', 20_000),
            new InvalidOperationException(new string('y', 20_000)));

        var current = Path.Combine(_directory, "krot-current.log");
        Assert.InRange(new FileInfo(current).Length, 1, maxFileBytes);
        Assert.Contains(
            "[entry truncated]",
            File.ReadAllText(current));
    }

    [Fact]
    public void Entry_IsSingleLineAndErrorIsAlwaysWritten()
    {
        var log = new RotatingFileLogService(false, _directory);

        log.Error("bad\r\nevent", "bad\tmessage", new InvalidOperationException("line1\r\nline2"));

        var lines = File.ReadAllLines(Path.Combine(_directory, "krot-current.log"));
        Assert.Single(lines);
        Assert.Contains("bad  event", lines[0]);
        Assert.Contains("bad message", lines[0]);
        Assert.Contains("line1  line2", lines[0]);
    }

    [Fact]
    public void WriteFailure_DoesNotEscapeIntoApplication()
    {
        Directory.CreateDirectory(_directory);
        var invalidDirectory = Path.Combine(_directory, "not-a-directory");
        File.WriteAllText(invalidDirectory, "file");
        var log = new RotatingFileLogService(false, invalidDirectory);

        var exception = Record.Exception(
            () => log.Error("test", "logging path is unavailable"));

        Assert.Null(exception);
    }

    [Fact]
    public void ConcurrentWrites_RemainWithinRotationBounds()
    {
        const int maxFileBytes = 2048;
        var log = new RotatingFileLogService(
            true,
            _directory,
            maxFileBytes,
            maxFiles: 3);

        Parallel.For(
            0,
            200,
            index => log.Detail($"event.{index}", new string('x', 120)));

        var files = Directory.GetFiles(_directory, "krot-*.log");
        Assert.InRange(files.Length, 1, 3);
        Assert.All(
            files,
            file => Assert.InRange(new FileInfo(file).Length, 1, maxFileBytes));
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }
}
