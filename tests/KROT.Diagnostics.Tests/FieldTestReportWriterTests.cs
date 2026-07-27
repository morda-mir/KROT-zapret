using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using KROT.Diagnostics.FieldTesting;
using Xunit;

namespace KROT.Diagnostics.Tests;

public sealed class FieldTestReportWriterTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "KROT-field-report-tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Report_HasNoSensitiveDataFieldsAndIsWrittenAtomically()
    {
        var path = Path.Combine(_directory, "report.json");
        var report = new FieldTestReport
        {
            Network = new NetworkEnvironmentSummary
            {
                FingerprintSha256 = new string('a', 64)
            }
        };

        await new FieldTestReportWriter().WriteAsync(report, path, CancellationToken.None);
        var json = File.ReadAllText(path).ToLowerInvariant();

        Assert.DoesNotContain("\"token\"", json);
        Assert.DoesNotContain("\"cookie\"", json);
        Assert.DoesNotContain("\"publicip\"", json);
        Assert.DoesNotContain("\"ssid\"", json);
        Assert.DoesNotContain("\"mac\"", json);
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

