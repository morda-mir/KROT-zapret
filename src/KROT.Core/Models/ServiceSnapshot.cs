using KROT.Core.States;

namespace KROT.Core.Models;

public sealed class ServiceSnapshot
{
    public AppState AppState { get; set; } = AppState.Off;

    public string MessageKey { get; set; } = "Status.Off";

    public bool IsFakeRuntime { get; set; } = true;
}

