using System;
using System.IO;
using System.Text;
using KROT.Zapret.Runtime;
using Xunit;

namespace KROT.Zapret.Tests;

public sealed class FileIntegrityVerifierTests : IDisposable
{
    private readonly string _path = Path.Combine(
        Path.GetTempPath(),
        "KROT-hash-" + Guid.NewGuid().ToString("N") + ".bin");

    [Fact]
    public void Verify_AcceptsMatchingSha256AndRejectsMismatch()
    {
        File.WriteAllBytes(_path, Encoding.UTF8.GetBytes("KROT"));
        var verifier = new FileIntegrityVerifier();

        Assert.True(verifier.Verify(
            _path,
            "8612adc6e9494bbe3f407429d7bb76ca722fb9803e726530267ffc8c1773d2bb"));
        Assert.False(verifier.Verify(_path, new string('0', 64)));
    }

    public void Dispose()
    {
        if (File.Exists(_path))
        {
            File.Delete(_path);
        }
    }
}
