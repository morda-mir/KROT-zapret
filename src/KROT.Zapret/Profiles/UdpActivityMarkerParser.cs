using System;

namespace KROT.Zapret.Profiles;

public sealed class UdpActivityMarker
{
    public string Channel { get; set; } = string.Empty;
}

public static class UdpActivityMarkerParser
{
    private const string Prefix = "KROT_UDP_ACTIVITY|";

    public static bool TryParse(string? line, out UdpActivityMarker marker)
    {
        marker = new UdpActivityMarker();
        if (string.IsNullOrWhiteSpace(line)
            || line?.StartsWith(Prefix, StringComparison.Ordinal) != true)
        {
            return false;
        }

        var parts = line!.Split('|');
        if (parts.Length != 2
            || parts[1] is not ("discord_voice" or "youtube_quic"))
        {
            return false;
        }

        marker.Channel = parts[1];
        return true;
    }
}
