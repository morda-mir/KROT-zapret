using System.Collections.Generic;

namespace KROT.Zapret.Runtime;

public sealed class RuntimeManifest
{
    public int SchemaVersion { get; set; } = 1;

    public string ActiveZapret2 { get; set; } = string.Empty;

    public string LegacyZapret1 { get; set; } = string.Empty;

    public List<RuntimeFileEntry> Files { get; set; } = new();
}

public sealed class RuntimeFileEntry
{
    public string RuntimeId { get; set; } = string.Empty;

    public string RelativePath { get; set; } = string.Empty;

    public string Version { get; set; } = string.Empty;

    public string Sha256 { get; set; } = string.Empty;

    public string SourceUrl { get; set; } = string.Empty;
}
