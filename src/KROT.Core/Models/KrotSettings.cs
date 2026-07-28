using System.Collections.Generic;

namespace KROT.Core.Models;

public sealed class KrotSettings
{
    public const int CurrentSchemaVersion = 2;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    public string Language { get; set; } = "ru";

    public bool AutoStart { get; set; }

    public bool RestoreEnabledState { get; set; }

    public bool DetailedLogs { get; set; }

    public List<ServiceSelection> Services { get; set; } = new()
    {
        new ServiceSelection { Id = ServiceId.Discord, IsEnabled = true },
        new ServiceSelection { Id = ServiceId.YouTube, IsEnabled = true }
    };
}
