using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Threading;
using System.Threading.Tasks;
using KROT.Core.Contracts;
using KROT.Core.Models;
using KROT.Zapret.Profiles;

namespace KROT.Service.Hosting;

public sealed class HttpPresetReachabilityProbe : IPresetReachabilityProbe
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(4);
    private static readonly IReadOnlyDictionary<ServiceId, EndpointDefinition[]> Endpoints =
        new Dictionary<ServiceId, EndpointDefinition[]>
        {
            [ServiceId.Discord] = new[]
            {
                Optional("api", "https://discord.com/api/v10/gateway"),
                Required("gateway", "https://gateway.discord.gg/"),
                Required("media", "https://cdn.discordapp.com/")
            },
            [ServiceId.YouTube] = new[]
            {
                Required("site", "https://www.youtube.com/generate_204"),
                Required("api", "https://youtubei.googleapis.com/generate_204"),
                Required(
                    "video",
                    "https://redirector.googlevideo.com/report_mapping"),
                Optional("images", "https://i.ytimg.com/generate_204")
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
        client.DefaultRequestHeaders.UserAgent.ParseAdd("KROT-zapret/1.0");

        var results = await Task.WhenAll(
                endpoints.Select(endpoint =>
                    ProbeEndpointAsync(client, endpoint, timeout.Token)))
            .ConfigureAwait(false);

        cancellationToken.ThrowIfCancellationRequested();
        var reachable = RequiredEndpointsReachable(results);
        var summary = string.Join(
            ", ",
            results.Select(result =>
                $"{result.Role}:{result.Host}={result.Outcome}"));
        _log.Detail(
            "preset.probe",
            reachable
                ? $"{serviceId} required endpoints reachable: {summary}."
                : $"{serviceId} required endpoints unavailable: {summary}.");
        return reachable;
    }

    internal static bool RequiredEndpointsReachable(
        IEnumerable<EndpointProbeResult> results)
    {
        var materialized = results.ToList();
        return materialized.Any(result => result.Required)
               && materialized
                   .Where(result => result.Required)
                   .All(result => result.Reachable);
    }

    internal static string DescribeRequestFailure(HttpRequestException exception)
    {
        WebExceptionStatus? webStatus = null;
        for (Exception? current = exception; current != null; current = current.InnerException)
        {
            if (current is SocketException socketException)
            {
                return DescribeSocketFailure(socketException.SocketErrorCode);
            }

            if (current is AuthenticationException)
            {
                return "tls-authentication";
            }

            if (current is WebException webException)
            {
                webStatus = webException.Status;
            }
        }

        if (webStatus.HasValue)
        {
            return DescribeWebFailure(webStatus.Value);
        }

        var root = exception.GetBaseException();
        return $"http-request-{root.GetType().Name.ToLowerInvariant()}";
    }

    private static async Task<EndpointProbeResult> ProbeEndpointAsync(
        HttpClient client,
        EndpointDefinition endpoint,
        CancellationToken cancellationToken)
    {
        try
        {
            using var response = await client
                .GetAsync(
                    endpoint.Uri,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken)
                .ConfigureAwait(false);
            return EndpointProbeResult.Response(
                endpoint,
                (int)response.StatusCode);
        }
        catch (TaskCanceledException)
        {
            return EndpointProbeResult.Failed(endpoint, "timeout");
        }
        catch (HttpRequestException exception)
        {
            return EndpointProbeResult.Failed(
                endpoint,
                DescribeRequestFailure(exception));
        }
    }

    private static string DescribeSocketFailure(SocketError error) =>
        error switch
        {
            SocketError.HostNotFound => "dns-host-not-found",
            SocketError.NoData => "dns-no-data",
            SocketError.TryAgain => "dns-try-again",
            SocketError.TimedOut => "connect-timeout",
            SocketError.ConnectionRefused => "connect-refused",
            SocketError.ConnectionReset => "connection-reset",
            SocketError.HostUnreachable => "host-unreachable",
            SocketError.NetworkUnreachable => "network-unreachable",
            _ => $"socket-{error.ToString().ToLowerInvariant()}"
        };

    private static string DescribeWebFailure(WebExceptionStatus status) =>
        status switch
        {
            WebExceptionStatus.NameResolutionFailure => "dns-name-resolution",
            WebExceptionStatus.ProxyNameResolutionFailure => "proxy-dns",
            WebExceptionStatus.ConnectFailure => "connect-failure",
            WebExceptionStatus.Timeout => "connect-timeout",
            WebExceptionStatus.TrustFailure => "tls-trust-failure",
            WebExceptionStatus.SecureChannelFailure => "tls-secure-channel",
            WebExceptionStatus.ConnectionClosed => "connection-closed",
            _ => $"web-{status.ToString().ToLowerInvariant()}"
        };

    private static EndpointDefinition Required(string role, string uri) =>
        new(role, new Uri(uri), required: true);

    private static EndpointDefinition Optional(string role, string uri) =>
        new(role, new Uri(uri), required: false);

    internal sealed class EndpointDefinition
    {
        public EndpointDefinition(string role, Uri uri, bool required)
        {
            Role = role;
            Uri = uri;
            Required = required;
        }

        public string Role { get; }

        public Uri Uri { get; }

        public bool Required { get; }
    }

    internal sealed class EndpointProbeResult
    {
        private EndpointProbeResult(
            string role,
            string host,
            bool required,
            bool reachable,
            int statusCode,
            string outcome)
        {
            Role = role;
            Host = host;
            Required = required;
            Reachable = reachable;
            StatusCode = statusCode;
            Outcome = outcome;
        }

        public string Role { get; }

        public string Host { get; }

        public bool Required { get; }

        public bool Reachable { get; }

        public int StatusCode { get; }

        public string Outcome { get; }

        public static EndpointProbeResult Response(
            EndpointDefinition endpoint,
            int statusCode) =>
            new(
                endpoint.Role,
                endpoint.Uri.Host,
                endpoint.Required,
                reachable: statusCode < 500,
                statusCode,
                $"http-{statusCode}");

        public static EndpointProbeResult Failed(
            EndpointDefinition endpoint,
            string outcome) =>
            new(
                endpoint.Role,
                endpoint.Uri.Host,
                endpoint.Required,
                reachable: false,
                statusCode: 0,
                outcome);
    }
}
