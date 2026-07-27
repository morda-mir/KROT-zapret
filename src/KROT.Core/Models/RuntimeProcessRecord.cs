using System;

namespace KROT.Core.Models;

public sealed class RuntimeProcessRecord
{
    public string Role { get; set; } = string.Empty;

    public int ProcessId { get; set; }

    public DateTime StartedUtc { get; set; }

    public string ExecutableSha256 { get; set; } = string.Empty;

    public Guid OwnershipMarker { get; set; }
}

