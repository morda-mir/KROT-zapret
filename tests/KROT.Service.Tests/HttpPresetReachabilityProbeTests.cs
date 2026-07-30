using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using KROT.Service.Hosting;
using Xunit;

namespace KROT.Service.Tests;

public sealed class HttpPresetReachabilityProbeTests
{
    [Fact]
    public void RequiredEndpointsReachable_RejectsOptionalOnlySuccess()
    {
        var results = new[]
        {
            Failed("site", "www.youtube.com", required: true),
            Failed("api", "youtubei.googleapis.com", required: true),
            Failed("video", "redirector.googlevideo.com", required: true),
            Response("images", "i.ytimg.com", required: false)
        };

        Assert.False(
            HttpPresetReachabilityProbe.RequiredEndpointsReachable(results));
    }

    [Fact]
    public void RequiredEndpointsReachable_RequiresEveryRequiredEndpoint()
    {
        var partial = new[]
        {
            Response("site", "www.youtube.com", required: true),
            Response("api", "youtubei.googleapis.com", required: true),
            Failed("video", "redirector.googlevideo.com", required: true)
        };
        var complete = new[]
        {
            Response("site", "www.youtube.com", required: true),
            Response("api", "youtubei.googleapis.com", required: true),
            Response("video", "redirector.googlevideo.com", required: true),
            Failed("images", "i.ytimg.com", required: false)
        };

        Assert.False(
            HttpPresetReachabilityProbe.RequiredEndpointsReachable(partial));
        Assert.True(
            HttpPresetReachabilityProbe.RequiredEndpointsReachable(complete));
    }

    [Theory]
    [MemberData(nameof(FailureCases))]
    public void DescribeRequestFailure_ReportsUsefulNetworkCategory(
        Exception inner,
        string expected)
    {
        var exception = new HttpRequestException("probe failed", inner);

        Assert.Equal(
            expected,
            HttpPresetReachabilityProbe.DescribeRequestFailure(exception));
    }

    public static IEnumerable<object[]> FailureCases()
    {
        yield return new object[]
        {
            new SocketException((int)SocketError.HostNotFound),
            "dns-host-not-found"
        };
        yield return new object[]
        {
            new SocketException((int)SocketError.ConnectionReset),
            "connection-reset"
        };
        yield return new object[]
        {
            new WebException("TLS", WebExceptionStatus.TrustFailure),
            "tls-trust-failure"
        };
        yield return new object[]
        {
            new WebException(
                "connect",
                new SocketException((int)SocketError.HostNotFound),
                WebExceptionStatus.ConnectFailure,
                response: null),
            "dns-host-not-found"
        };
    }

    private static HttpPresetReachabilityProbe.EndpointProbeResult Response(
        string role,
        string host,
        bool required) =>
        HttpPresetReachabilityProbe.EndpointProbeResult.Response(
            Definition(role, host, required),
            statusCode: 204);

    private static HttpPresetReachabilityProbe.EndpointProbeResult Failed(
        string role,
        string host,
        bool required) =>
        HttpPresetReachabilityProbe.EndpointProbeResult.Failed(
            Definition(role, host, required),
            "timeout");

    private static HttpPresetReachabilityProbe.EndpointDefinition Definition(
        string role,
        string host,
        bool required) =>
        new(role, new Uri($"https://{host}/"), required);
}
