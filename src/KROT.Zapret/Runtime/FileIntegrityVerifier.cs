using System.IO;
using System.Linq;
using System.Security.Cryptography;

namespace KROT.Zapret.Runtime;

public sealed class FileIntegrityVerifier
{
    public bool Verify(string path, string expectedSha256)
    {
        if (!File.Exists(path) || string.IsNullOrWhiteSpace(expectedSha256))
        {
            return false;
        }

        using var stream = File.OpenRead(path);
        using var sha256 = SHA256.Create();
        var actual = string.Concat(sha256.ComputeHash(stream).Select(x => x.ToString("x2")));
        return string.Equals(actual, expectedSha256.Trim(), System.StringComparison.OrdinalIgnoreCase);
    }
}

