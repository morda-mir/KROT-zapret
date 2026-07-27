using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace KROT.Diagnostics.FieldTesting;

public sealed class FieldTestRunner
{
    private readonly NetworkEnvironmentInspector _environmentInspector;
    private readonly IEndpointProbe _probe;
    private readonly IReadOnlyList<FieldTestEndpoint> _endpoints;

    public FieldTestRunner(
        NetworkEnvironmentInspector? environmentInspector = null,
        IEndpointProbe? probe = null,
        IReadOnlyList<FieldTestEndpoint>? endpoints = null)
    {
        _environmentInspector = environmentInspector ?? new NetworkEnvironmentInspector();
        _probe = probe ?? new EndpointProbe();
        _endpoints = endpoints ?? FieldTestCatalog.Default;
    }

    public async Task<FieldTestReport> RunAsync(
        IProgress<FieldTestProgress>? progress,
        CancellationToken cancellationToken)
    {
        var report = new FieldTestReport
        {
            StartedUtc = DateTime.UtcNow,
            Network = _environmentInspector.Inspect()
        };
        report.BaselineValidity = report.Network.VpnLikely
            ? "VPN_OR_PROXY_DETECTED"
            : "NO_VPN_DETECTED";

        var tasks = _endpoints.Select(
            endpoint => _probe.ProbeAsync(endpoint, progress, cancellationToken));
        var results = await Task.WhenAll(tasks).ConfigureAwait(false);
        report.Services = new List<ServiceProbeResult>(results);
        report.CompletedUtc = DateTime.UtcNow;
        return report;
    }
}

