using System;
using System.Collections.Generic;

namespace KROT.Diagnostics.FieldTesting;

public sealed class FieldTestReport
{
    public int SchemaVersion { get; set; } = 1;

    public string KrotVersion { get; set; } = "1.0";

    public DateTime StartedUtc { get; set; }

    public DateTime CompletedUtc { get; set; }

    public string BaselineValidity { get; set; } = "Unknown";

    public NetworkEnvironmentSummary Network { get; set; } = new();

    public List<ServiceProbeResult> Services { get; set; } = new();
}

public sealed class NetworkEnvironmentSummary
{
    public bool VpnLikely { get; set; }

    public bool SystemProxyDetected { get; set; }

    public int ActiveAdapterCount { get; set; }

    public string FingerprintSha256 { get; set; } = string.Empty;

    public List<AdapterSummary> Adapters { get; set; } = new();
}

public sealed class AdapterSummary
{
    public string IdSha256 { get; set; } = string.Empty;

    public string InterfaceType { get; set; } = string.Empty;

    public bool HasDefaultGateway { get; set; }

    public bool LikelyVpn { get; set; }

    public int DnsServerCount { get; set; }
}

public sealed class ServiceProbeResult
{
    public string ServiceId { get; set; } = string.Empty;

    public string Host { get; set; } = string.Empty;

    public ProbeStepResult Dns { get; set; } = ProbeStepResult.Skipped("NOT_STARTED");

    public ProbeStepResult Tcp { get; set; } = ProbeStepResult.Skipped("NOT_STARTED");

    public ProbeStepResult Tls { get; set; } = ProbeStepResult.Skipped("NOT_STARTED");

    public ProbeStepResult Http { get; set; } = ProbeStepResult.Skipped("NOT_STARTED");

    public ProbeStepResult Quic { get; set; } = ProbeStepResult.Skipped("UNSUPPORTED_NET48");
}

public sealed class ProbeStepResult
{
    public string Status { get; set; } = "skipped";

    public string Code { get; set; } = string.Empty;

    public long DurationMs { get; set; }

    public string Detail { get; set; } = string.Empty;

    public static ProbeStepResult Success(long durationMs, string detail = "") =>
        new() { Status = "success", Code = "OK", DurationMs = durationMs, Detail = detail };

    public static ProbeStepResult Failed(string code, long durationMs, string detail = "") =>
        new() { Status = "failed", Code = code, DurationMs = durationMs, Detail = detail };

    public static ProbeStepResult Skipped(string code) =>
        new() { Status = "skipped", Code = code };
}

public sealed class FieldTestProgress
{
    public string ServiceId { get; set; } = string.Empty;

    public string Stage { get; set; } = string.Empty;

    public string Message { get; set; } = string.Empty;
}
