using System;

namespace KROT.Zapret.Profiles;

public sealed class UdpWinnerMarker
{
    public string Channel { get; set; } = string.Empty;

    public string StrategyId { get; set; } = string.Empty;
}

public static class UdpWinnerMarkerParser
{
    private const string Prefix = "KROT_UDP_WINNER|";

    public static bool TryParse(string? line, out UdpWinnerMarker marker)
    {
        marker = new UdpWinnerMarker();
        if (string.IsNullOrWhiteSpace(line)
            || line?.StartsWith(Prefix, StringComparison.Ordinal) != true)
        {
            return false;
        }

        var parts = line!.Split('|');
        if (parts.Length != 3)
        {
            return false;
        }

        var valid = parts[1] switch
        {
            "youtube_quic" => BuiltInStrategyCatalog.IsQuic(parts[2]),
            "discord_voice" => BuiltInStrategyCatalog.IsVoice(parts[2]),
            _ => false
        };
        if (!valid)
        {
            return false;
        }

        marker.Channel = parts[1];
        marker.StrategyId = parts[2];
        return true;
    }
}
