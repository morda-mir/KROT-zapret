using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using KROT.Core.Ipc;
using Xunit;

namespace KROT.Core.Tests;

public sealed class BoundedUtf8LineReaderTests
{
    [Fact]
    public async Task ReadAsync_ReturnsSingleUtf8LineWithoutTerminator()
    {
        using var stream = new MemoryStream(
            Encoding.UTF8.GetBytes("{\"message\":\"готово\"}\r\nignored"));

        var line = await BoundedUtf8LineReader.ReadAsync(
            stream,
            maxByteCount: 128,
            timeout: TimeSpan.FromSeconds(1),
            cancellationToken: CancellationToken.None);

        Assert.Equal("{\"message\":\"готово\"}", line);
    }

    [Fact]
    public async Task ReadAsync_RejectsOversizedMessages()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("12345\n"));

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            BoundedUtf8LineReader.ReadAsync(
                stream,
                maxByteCount: 4,
                timeout: TimeSpan.FromSeconds(1),
                cancellationToken: CancellationToken.None));
    }

    [Fact]
    public async Task ReadAsync_RejectsInvalidUtf8()
    {
        using var stream = new MemoryStream(new byte[] { 0xff, (byte)'\n' });

        await Assert.ThrowsAsync<DecoderFallbackException>(() =>
            BoundedUtf8LineReader.ReadAsync(
                stream,
                maxByteCount: 4,
                timeout: TimeSpan.FromSeconds(1),
                cancellationToken: CancellationToken.None));
    }
}
