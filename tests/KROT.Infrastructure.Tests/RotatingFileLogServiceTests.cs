using System;
using System.IO;
using System.Linq;
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
        var log = new RotatingFileLogService(true, _directory, maxFileBytes: 16, maxFiles: 3);

        for (var index = 0; index < 10; index++)
        {
            log.Info("test.event", new string('x', 40));
        }

        Assert.InRange(Directory.GetFiles(_directory, "krot-*.log").Count(), 1, 3);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }
}

