using KROT.Core.Models;

namespace KROT.Core.Ipc;

public static class ServiceProtocol
{
    public const int Version = 1;

    public static string PipeNameForSid(string sid) =>
        $"KROT.Service.v{Version}.{sid.Replace('-', '_')}";
}

public sealed class ServiceRequest
{
    public int ProtocolVersion { get; set; } = ServiceProtocol.Version;

    public string Command { get; set; } = string.Empty;
}

public sealed class ServiceResponse
{
    public bool Success { get; set; }

    public string ErrorCode { get; set; } = string.Empty;

    public ServiceSnapshot Snapshot { get; set; } = new();
}

