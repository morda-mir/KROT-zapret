using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Threading;
using System.Threading.Tasks;

namespace KROT.Diagnostics.FieldTesting;

public interface IEndpointProbe
{
    Task<ServiceProbeResult> ProbeAsync(
        FieldTestEndpoint endpoint,
        IProgress<FieldTestProgress>? progress,
        CancellationToken cancellationToken);
}

public sealed class EndpointProbe : IEndpointProbe
{
    private static readonly TimeSpan DnsTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan HttpTimeout = TimeSpan.FromSeconds(8);

    public async Task<ServiceProbeResult> ProbeAsync(
        FieldTestEndpoint endpoint,
        IProgress<FieldTestProgress>? progress,
        CancellationToken cancellationToken)
    {
        var result = new ServiceProbeResult
        {
            ServiceId = endpoint.ServiceId,
            Host = endpoint.Host
        };

        progress?.Report(Progress(endpoint, "dns"));
        result.Dns = await ProbeDnsAsync(endpoint.Host, cancellationToken).ConfigureAwait(false);
        if (result.Dns.Status != "success")
        {
            result.Tcp = ProbeStepResult.Skipped("DNS_FAILED");
            result.Tls = ProbeStepResult.Skipped("DNS_FAILED");
            result.Http = ProbeStepResult.Skipped("DNS_FAILED");
            return result;
        }

        progress?.Report(Progress(endpoint, "tcp"));
        result.Tcp = await ProbeTcpAsync(endpoint.Host, cancellationToken).ConfigureAwait(false);
        if (result.Tcp.Status != "success")
        {
            result.Tls = ProbeStepResult.Skipped("TCP_FAILED");
            result.Http = ProbeStepResult.Skipped("TCP_FAILED");
            return result;
        }

        progress?.Report(Progress(endpoint, "tls"));
        result.Tls = await ProbeTlsAsync(endpoint.Host, cancellationToken).ConfigureAwait(false);

        progress?.Report(Progress(endpoint, "http"));
        result.Http = await ProbeHttpAsync(endpoint, cancellationToken).ConfigureAwait(false);
        progress?.Report(Progress(endpoint, "complete"));
        return result;
    }

    private static async Task<ProbeStepResult> ProbeDnsAsync(string host, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var addresses = await WithTimeout(
                Dns.GetHostAddressesAsync(host),
                DnsTimeout,
                cancellationToken).ConfigureAwait(false);
            stopwatch.Stop();
            return addresses.Length == 0
                ? ProbeStepResult.Failed("DNS_EMPTY", stopwatch.ElapsedMilliseconds)
                : ProbeStepResult.Success(stopwatch.ElapsedMilliseconds, $"addresses={addresses.Length}");
        }
        catch (TimeoutException)
        {
            return ProbeStepResult.Failed("DNS_TIMEOUT", stopwatch.ElapsedMilliseconds);
        }
        catch (SocketException ex)
        {
            return ProbeStepResult.Failed($"DNS_{ex.SocketErrorCode}".ToUpperInvariant(), stopwatch.ElapsedMilliseconds);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return ProbeStepResult.Failed("DNS_ERROR", stopwatch.ElapsedMilliseconds, ex.GetType().Name);
        }
    }

    private static async Task<ProbeStepResult> ProbeTcpAsync(string host, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        using var client = new TcpClient();
        try
        {
            await WithTimeout(
                client.ConnectAsync(host, 443),
                ConnectTimeout,
                cancellationToken).ConfigureAwait(false);
            stopwatch.Stop();
            return ProbeStepResult.Success(stopwatch.ElapsedMilliseconds, "port=443");
        }
        catch (TimeoutException)
        {
            client.Close();
            return ProbeStepResult.Failed("TCP_TIMEOUT", stopwatch.ElapsedMilliseconds, "port=443");
        }
        catch (SocketException ex)
        {
            return ProbeStepResult.Failed($"TCP_{ex.SocketErrorCode}".ToUpperInvariant(), stopwatch.ElapsedMilliseconds, "port=443");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return ProbeStepResult.Failed("TCP_ERROR", stopwatch.ElapsedMilliseconds, ex.GetType().Name);
        }
    }

    private static async Task<ProbeStepResult> ProbeTlsAsync(string host, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        using var client = new TcpClient();
        try
        {
            await WithTimeout(
                client.ConnectAsync(host, 443),
                ConnectTimeout,
                cancellationToken).ConfigureAwait(false);
            using var stream = new SslStream(client.GetStream(), leaveInnerStreamOpen: false);
            await WithTimeout(
                stream.AuthenticateAsClientAsync(host),
                ConnectTimeout,
                cancellationToken).ConfigureAwait(false);
            stopwatch.Stop();
            return ProbeStepResult.Success(stopwatch.ElapsedMilliseconds, $"protocol={stream.SslProtocol}");
        }
        catch (TimeoutException)
        {
            client.Close();
            return ProbeStepResult.Failed("TLS_TIMEOUT", stopwatch.ElapsedMilliseconds);
        }
        catch (AuthenticationException)
        {
            return ProbeStepResult.Failed("TLS_CERTIFICATE_OR_HANDSHAKE", stopwatch.ElapsedMilliseconds);
        }
        catch (SocketException ex)
        {
            return ProbeStepResult.Failed($"TLS_{ex.SocketErrorCode}".ToUpperInvariant(), stopwatch.ElapsedMilliseconds);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return ProbeStepResult.Failed("TLS_ERROR", stopwatch.ElapsedMilliseconds, ex.GetType().Name);
        }
    }

    private static async Task<ProbeStepResult> ProbeHttpAsync(
        FieldTestEndpoint endpoint,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            using var handler = new HttpClientHandler
            {
                AllowAutoRedirect = true,
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
                UseCookies = false,
                UseProxy = false
            };
            using var client = new HttpClient(handler) { Timeout = HttpTimeout };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("KROT-FieldTest/0.1");
            using var response = await client.GetAsync(
                new Uri($"https://{endpoint.Host}{endpoint.Path}"),
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken).ConfigureAwait(false);

            var bytesRead = await ReadAtMostAsync(
                await response.Content.ReadAsStreamAsync().ConfigureAwait(false),
                4096,
                cancellationToken).ConfigureAwait(false);
            stopwatch.Stop();
            var status = (int)response.StatusCode;
            var detail = $"status={status};bytes={bytesRead}";
            return status < 500
                ? ProbeStepResult.Success(stopwatch.ElapsedMilliseconds, detail)
                : ProbeStepResult.Failed($"HTTP_{status}", stopwatch.ElapsedMilliseconds, detail);
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return ProbeStepResult.Failed("HTTP_TIMEOUT", stopwatch.ElapsedMilliseconds);
        }
        catch (HttpRequestException)
        {
            return ProbeStepResult.Failed("HTTP_REQUEST_ERROR", stopwatch.ElapsedMilliseconds);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return ProbeStepResult.Failed("HTTP_ERROR", stopwatch.ElapsedMilliseconds, ex.GetType().Name);
        }
    }

    private static async Task<int> ReadAtMostAsync(
        Stream stream,
        int maximumBytes,
        CancellationToken cancellationToken)
    {
        using (stream)
        {
            var buffer = new byte[maximumBytes];
            return await stream.ReadAsync(buffer, 0, buffer.Length, cancellationToken).ConfigureAwait(false);
        }
    }

    private static FieldTestProgress Progress(FieldTestEndpoint endpoint, string stage) =>
        new()
        {
            ServiceId = endpoint.ServiceId,
            Stage = stage,
            Message = $"{endpoint.ServiceId}: {stage}"
        };

    private static async Task<T> WithTimeout<T>(
        Task<T> operation,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        using var timeoutCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var delay = Task.Delay(timeout, timeoutCancellation.Token);
        var completed = await Task.WhenAny(operation, delay).ConfigureAwait(false);
        if (completed != operation)
        {
            cancellationToken.ThrowIfCancellationRequested();
            throw new TimeoutException();
        }

        timeoutCancellation.Cancel();
        return await operation.ConfigureAwait(false);
    }

    private static async Task WithTimeout(
        Task operation,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        using var timeoutCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var delay = Task.Delay(timeout, timeoutCancellation.Token);
        var completed = await Task.WhenAny(operation, delay).ConfigureAwait(false);
        if (completed != operation)
        {
            cancellationToken.ThrowIfCancellationRequested();
            throw new TimeoutException();
        }

        timeoutCancellation.Cancel();
        await operation.ConfigureAwait(false);
    }
}

