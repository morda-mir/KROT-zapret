using System;
using System.Threading;
using System.Threading.Tasks;
using KROT.Diagnostics.FieldTesting;
using Xunit;

namespace KROT.Diagnostics.Tests;

public sealed class FieldTestRunnerTests
{
    [Fact]
    public async Task Runner_UsesInjectedProbeWithoutRealNetworkTraffic()
    {
        var endpoints = new[]
        {
            new FieldTestEndpoint("one", "one.example", "/"),
            new FieldTestEndpoint("two", "two.example", "/")
        };
        var runner = new FieldTestRunner(
            probe: new FakeEndpointProbe(),
            endpoints: endpoints);

        var report = await runner.RunAsync(progress: null, CancellationToken.None);

        Assert.Equal(2, report.Services.Count);
        Assert.All(report.Services, result => Assert.Equal("success", result.Http.Status));
        Assert.NotEmpty(report.Network.FingerprintSha256);
        Assert.Equal(64, report.Network.FingerprintSha256.Length);
    }

    private sealed class FakeEndpointProbe : IEndpointProbe
    {
        public Task<ServiceProbeResult> ProbeAsync(
            FieldTestEndpoint endpoint,
            IProgress<FieldTestProgress>? progress,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(new ServiceProbeResult
            {
                ServiceId = endpoint.ServiceId,
                Host = endpoint.Host,
                Dns = ProbeStepResult.Success(1),
                Tcp = ProbeStepResult.Success(1),
                Tls = ProbeStepResult.Success(1),
                Http = ProbeStepResult.Success(1)
            });
        }
    }
}

