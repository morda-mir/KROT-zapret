using System.Collections.Generic;
using KROT.Core.States;

namespace KROT.Core.Models;

public sealed class ServiceSnapshot
{
    public AppState AppState { get; set; } = AppState.Off;

    public string MessageKey { get; set; } = "Status.Off";

    public bool IsFakeRuntime { get; set; }

    public bool ExternalTunnelDetected { get; set; }

    public List<ChannelSnapshot> Channels { get; set; } = new();
}

public sealed class ChannelSnapshot
{
    public ServiceId ServiceId { get; set; }

    public string ChannelId { get; set; } = string.Empty;

    public ChannelState State { get; set; } = ChannelState.Unknown;

    public string StrategyId { get; set; } = string.Empty;
}
