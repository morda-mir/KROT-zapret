using System.Collections.Generic;

namespace KROT.Core.Models;

public sealed class KrotStartOptions
{
    public List<ServiceId> Services { get; set; } = new();

    public bool DetailedLogs { get; set; }

    public bool SkipVoice { get; set; }
}
