using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using KROT.Core.Contracts;
using KROT.Core.Models;
using KROT.Zapret.Profiles;

namespace KROT.Service.Hosting;

public sealed class HttpPresetReachabilityProbe : IPresetReachabilityProbe
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(4);
    private static readonly IReadOnlyDictionary<ServiceId, Uri[]> Endpoints =
        new Dictionary<ServiceId, Uri[]>
        {
            [ServiceId.Discord] = new[]
            {
                new Uri("https://discord.com/api/v10/gateway"),
                new Uri("https://gateway.discord.gg/"),
                new Uri("https://cdn.discordapp.com/")
            },
            [ServiceId.YouTube] = new[]
            {
                new Uri("https://www.youtube.com/generate_204"),
                new Uri("https://i.ytimg.com/generate_204"),
                new Uri("https://youtubei.googleapis.com/generate_204")
            }
        };
    private readonly ILogService _log;

    public HttpPresetReachabilityProbe(ILogService log)
    {
        _log = log;
    }

    public async Task<bool> CheckAsync(
        ServiceId serviceId,
        CancellationToken cancellationToken)
    {
        if (!Endpoints.TryGetValue(serviceId, out var endpoints))
        {
            return true;
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(Timeout);
        using var handler = new HttpClientHandler
        {
            AllowAutoRedirect = true,
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
            UseCookies = false,
            UseProxy = false
        };
        using var client = new HttpClient(handler)
        {
            Timeout = System.Threading.Timeout.InfiniteTimeSpan
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("KROT-zapret/0.1");

        var pending = endpoints
            .Select(uri => ProbeEndpointAsync(client, uri, timeout.Token))
            .ToList();
        var results = new List<EndpointProbeResult>();
        while (pending.Count > 0)
        {
            var completed = await Task.WhenAny(pending).ConfigureAwait(false);
            pending.Remove(completed);
            var result = await completed.ConfigureAwait(false);
            results.Add(result);
            if (!result.Reachable)
            {
                continue;
            }

            timeout.Cancel();
            await Task.WhenAll(pending).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            _log.Info(
                "preset.probe",
                $"{serviceId} reachable via {result.Host} (HTTP {result.StatusCode}).");
            return true;
        }

        cancellationToken.ThrowIfCancellationRequested();
        _log.Info(
            "preset.probe",
            $"{serviceId} endpoints unavailable: "
            + string.Join(
                ", ",
                results.Select(result => $"{result.Host}={result.Outcome}")));
        return false;
    }

    private static async Task<EndpointProbeResult> ProbeEndpointAsync(
        HttpClient client,
        Uri uri,
        CancellationToken cancellationToken)
    {
        try
        {
            using var response = await client
                .GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);
            return EndpointProbeResult.Response(uri.Host, (int)response.StatusCode);
        }
        catch (TaskCanceledException)
        {
            return EndpointProbeResult.Failed(uri.Host, "timeout");
        }
        catch (HttpRequestException)
        {
            return EndpointProbeResult.Failed(uri.Host, "request-failed");
        }
    }

    private sealed class EndpointProbeResult
    {
        private EndpointProbeResult(
            string host,
            bool reachable,
            int statusCode,
            string outcome)
        {
            Host = host;
            Reachable = reachable;
            StatusCode = statusCode;
            Outcome = outcome;
        }

        public string Host { get; }

        public bool Reachable { get; }

        public int StatusCode { get; }

        public string Outcome { get; }

        public static EndpointProbeResult Response(string host, int statusCode) =>
            new(host, reachable: true, statusCode, $"http-{statusCode}");

        public static EndpointProbeResult Failed(string host, string outcome) =>
            new(host, reachable: false, statusCode: 0, outcome);
    }
}
