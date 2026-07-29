using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace KROT.Core.Ipc;

public static class BoundedUtf8LineReader
{
    public static async Task<string> ReadAsync(
        Stream stream,
        int maxByteCount,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        if (maxByteCount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxByteCount));
        }

        using var timeoutCancellation =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCancellation.CancelAfter(timeout);
        using var buffer = new MemoryStream();
        var singleByte = new byte[1];

        try
        {
            while (true)
            {
                var bytesRead = await stream
                    .ReadAsync(
                        singleByte,
                        0,
                        singleByte.Length,
                        timeoutCancellation.Token)
                    .ConfigureAwait(false);
                if (bytesRead == 0)
                {
                    if (buffer.Length == 0)
                    {
                        throw new EndOfStreamException("The IPC peer closed the connection.");
                    }

                    break;
                }

                if (singleByte[0] == (byte)'\n')
                {
                    break;
                }

                if (buffer.Length >= maxByteCount)
                {
                    throw new InvalidDataException("The IPC message is too large.");
                }

                buffer.WriteByte(singleByte[0]);
            }
        }
        catch (OperationCanceledException)
            when (!cancellationToken.IsCancellationRequested
                  && timeoutCancellation.IsCancellationRequested)
        {
            throw new TimeoutException("Timed out while reading an IPC message.");
        }

        var payload = buffer.ToArray();
        var length = payload.Length;
        if (length > 0 && payload[length - 1] == (byte)'\r')
        {
            length--;
        }

        return new UTF8Encoding(
                encoderShouldEmitUTF8Identifier: false,
                throwOnInvalidBytes: true)
            .GetString(payload, 0, length);
    }
}
