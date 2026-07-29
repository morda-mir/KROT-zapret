namespace KROT.Core.Models;

public sealed class ReleaseUpdateInfo
{
    public string VersionTag { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public string ReleaseUrl { get; set; } = string.Empty;
}
