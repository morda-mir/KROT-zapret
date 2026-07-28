using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Threading;
using System.Threading.Tasks;
using KROT.Core.Contracts;

namespace KROT.Service.Hosting;

public interface IInternetAvailabilityProbe
{
    Task<bool> CheckAsync(CancellationToken cancellationToken);
}

public sealed class InternetAvailabilityProbe : IInternetAvailabilityProbe
{
    private static readonly Uri[] Endpoints =
    {
        new("https://www.msftconnecttest.com/connecttest.txt"),
        new("https://example.com/")
    };

    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(3);
    private readonly ILogService _log;

    public InternetAvailabilityProbe(ILogService log)
    {
        _log = log;
    }

    public async Task<bool> CheckAsync(CancellationToken cancellationToken)
    {
        if (!NetworkInterface.GetIsNetworkAvailable())
        {
            return false;
        }

        var checks = Endpoints
            .Select(endpoint => CheckEndpointAsync(endpoint, cancellationToken))
            .ToArray();
        var results = await Task.WhenAll(checks).ConfigureAwait(false);
        var available = results.Any(result => result);
        if (!available)
        {
            _log.Info(
                "runtime.internet.unavailable",
                "General internet probes did not receive a response.");
        }

        return available;
    }

    private static async Task<bool> CheckEndpointAsync(
        Uri endpoint,
        CancellationToken cancellationToken)
    {
        try
        {
            using var handler = new HttpClientHandler
            {
                AllowAutoRedirect = true,
                AutomaticDecompression =
                    DecompressionMethods.GZip | DecompressionMethods.Deflate,
                UseCookies = false,
                UseProxy = false
            };
            using var client = new HttpClient(handler) { Timeout = Timeout };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("KROT-zapret/0.1");
            using var response = await client
                .GetAsync(
                    endpoint,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken)
                .ConfigureAwait(false);
            return (int)response.StatusCode < 500;
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return false;
        }
        catch (HttpRequestException)
        {
            return false;
        }
    }
}
