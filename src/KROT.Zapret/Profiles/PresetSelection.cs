namespace KROT.Zapret.Profiles;

public sealed class PresetSelection
{
    public const string Direct = "direct";

    public string DiscordTcp { get; set; } = BuiltInStrategyCatalog.DefaultTcpId;

    public string YouTubeTcp { get; set; } = BuiltInStrategyCatalog.DefaultTcpId;

    public string YouTubeQuic { get; set; } = BuiltInStrategyCatalog.DefaultQuicId;

    public string DiscordVoice { get; set; } = BuiltInStrategyCatalog.DefaultVoiceId;

    public PresetSelection Clone() => new()
    {
        DiscordTcp = DiscordTcp,
        YouTubeTcp = YouTubeTcp,
        YouTubeQuic = YouTubeQuic,
        DiscordVoice = DiscordVoice
    };
}
