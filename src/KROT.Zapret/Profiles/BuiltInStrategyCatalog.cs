using System.Collections.Generic;
using System.Linq;

namespace KROT.Zapret.Profiles;

public sealed class StrategyDescriptor
{
    public StrategyDescriptor(string id, bool fast)
    {
        Id = id;
        Fast = fast;
    }

    public string Id { get; }

    public bool Fast { get; }
}

public static class BuiltInStrategyCatalog
{
    public const string DefaultTcpId = "tcp-01-alt-fakedsplit-ts";
    public const string DefaultQuicId = "quic-01-google-r6";
    public const string DefaultVoiceId = "voice-01-fake-r2";

    public static IReadOnlyList<StrategyDescriptor> Tcp { get; } = new[]
    {
        Fast(DefaultTcpId),
        Fast("tcp-02-fake-ts"),
        Fast("tcp-03-fake-badseq"),
        Fast("tcp-04-fake-multisplit-badseq"),
        Fast("tcp-05-multisplit-seqovl"),
        Deep("tcp-06-multisplit-sniext"),
        Deep("tcp-07-fake-hostfakesplit"),
        Deep("tcp-08-hostfakesplit"),
        Deep("tcp-09-auto-multisplit-badseq"),
        Deep("tcp-10-auto-multisplit-ts"),
        Deep("tcp-11-fake-multidisorder"),
        Deep("tcp-12-auto-fakedsplit"),
        Deep("tcp-13-multidisorder-sniext"),
        Deep("tcp-14-fakedsplit-midsld"),
        Deep("tcp-15-multisplit-iana"),
        Deep("tcp-16-fake-iana-r11"),
        Deep("tcp-17-multisplit-seqovl652"),
        Deep("tcp-18-fake-default-nomod"),
        Deep("tcp-19-auto-multidisorder")
    };

    public static IReadOnlyList<StrategyDescriptor> Quic { get; } = new[]
    {
        Fast(DefaultQuicId),
        Fast("quic-02-google-r11"),
        Fast("quic-03-facebook-r6"),
        Deep("quic-04-facebook-quiche-r11"),
        Deep("quic-05-googlevideo-kyber1-r6"),
        Deep("quic-06-googlevideo-kyber2-r10"),
        Deep("quic-07-rutracker-kyber-r8"),
        Deep("quic-08-vk-r12")
    };

    public static IReadOnlyList<StrategyDescriptor> Voice { get; } = new[]
    {
        Fast(DefaultVoiceId),
        Fast("voice-02-fake-r6"),
        Fast("voice-03-fake-r10"),
        Fast("voice-04-fake-ttl1-r6"),
        Deep("voice-05-fake-autottl-r6"),
        Deep("voice-06-udplen-2"),
        Deep("voice-07-udplen-8"),
        Deep("voice-08-fake-r4-udplen2"),
        Deep("voice-09-discovery-blob-r6"),
        Deep("voice-10-dtls-blob-r6")
    };

    public static bool IsTcp(string id) =>
        id == PresetSelection.Direct || Tcp.Any(item => item.Id == id);

    public static bool IsQuic(string id) =>
        id == PresetSelection.Direct || Quic.Any(item => item.Id == id);

    public static bool IsVoice(string id) =>
        id == PresetSelection.Direct || Voice.Any(item => item.Id == id);

    private static StrategyDescriptor Fast(string id) => new(id, fast: true);

    private static StrategyDescriptor Deep(string id) => new(id, fast: false);
}
