using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using KROT.Core.Models;

namespace KROT.Zapret.Profiles;

public sealed class BuiltInPresetCatalog
{
    private const string LegacyRuntimeId = "zapret1-v72.13";
    private readonly string _runtimeRoot;

    public BuiltInPresetCatalog(string runtimeRoot)
    {
        _runtimeRoot = runtimeRoot;
    }

    public ZapretRuntimePlan Build(
        KrotStartOptions options,
        PresetSelection? selection = null)
    {
        selection ??= new PresetSelection();
        Validate(selection);

        var selected = new HashSet<ServiceId>(options.Services);
        var main = new List<string>();
        var profiles = new List<IReadOnlyList<string>>();
        var presetIds = new List<string>();

        if (selected.Contains(ServiceId.Discord)
            && selection.DiscordTcp != PresetSelection.Direct)
        {
            profiles.Add(DiscordMediaProfile(selection.DiscordTcp));
            profiles.Add(TlsProfile("discord.txt", selection.DiscordTcp, includeHttp: true));
            presetIds.Add($"discord-tcp:{selection.DiscordTcp}");
        }

        if (selected.Contains(ServiceId.YouTube)
            && selection.YouTubeTcp != PresetSelection.Direct)
        {
            profiles.Add(YouTubeTlsProfile(selection.YouTubeTcp));
            presetIds.Add($"youtube-tcp:{selection.YouTubeTcp}");
        }

        if (profiles.Count > 0)
        {
            var tcpPorts = selected.Contains(ServiceId.Discord)
                           && selection.DiscordTcp != PresetSelection.Direct
                ? "80,443,2053,2083,2087,2096,8443"
                : "80,443";
            main.Add($"--wf-tcp={tcpPorts}");
            AddProfiles(main, profiles);
        }

        var udpAdaptive = BuildAdaptiveUdpPlan(
            selected,
            options,
            selection,
            presetIds);

        return new ZapretRuntimePlan
        {
            MainArguments = main,
            VoiceArguments = udpAdaptive,
            PresetIds = presetIds
        };
    }

    private IReadOnlyList<string> DiscordMediaProfile(string strategyId)
    {
        var result = new List<string>
        {
            "--filter-tcp=2053,2083,2087,2096,8443",
            "--hostlist-domains=discord.media"
        };
        result.AddRange(TcpStrategyArguments(strategyId, includeHttp: false));
        return result;
    }

    private IReadOnlyList<string> YouTubeTlsProfile(string strategyId)
    {
        var result = new List<string>
        {
            "--filter-tcp=443",
            $"--hostlist={RuntimeFile("hostlists", "youtube.txt")}",
            "--ip-id=zero"
        };
        result.AddRange(TcpStrategyArguments(strategyId, includeHttp: false));
        return result;
    }

    private IReadOnlyList<string> TlsProfile(
        string hostlist,
        string strategyId,
        bool includeHttp)
    {
        var result = new List<string>
        {
            "--filter-tcp=80,443",
            $"--hostlist={RuntimeFile("hostlists", hostlist)}"
        };
        result.AddRange(TcpStrategyArguments(strategyId, includeHttp));
        return result;
    }

    private IReadOnlyList<string> TcpStrategyArguments(
        string strategyId,
        bool includeHttp)
    {
        var google = LegacyFake("tls_clienthello_www_google_com.bin");
        var iana = LegacyFake("tls_clienthello_iana_org.bin");
        var result = strategyId switch
        {
            "tcp-01-alt-fakedsplit-ts" => new List<string>
            {
                "--dpi-desync=fake,fakedsplit",
                "--dpi-desync-repeats=6",
                "--dpi-desync-fooling=ts",
                "--dpi-desync-fakedsplit-pattern=0x00",
                $"--dpi-desync-fake-tls={google}"
            },
            "tcp-02-fake-ts" => new List<string>
            {
                "--dpi-desync=fake",
                "--dpi-desync-repeats=6",
                "--dpi-desync-fooling=ts",
                $"--dpi-desync-fake-tls={google}"
            },
            "tcp-03-fake-badseq" => new List<string>
            {
                "--dpi-desync=fake",
                "--dpi-desync-repeats=6",
                "--dpi-desync-fooling=badseq",
                "--dpi-desync-badseq-increment=2",
                $"--dpi-desync-fake-tls={google}"
            },
            "tcp-04-fake-multisplit-badseq" => new List<string>
            {
                "--dpi-desync=fake,multisplit",
                "--dpi-desync-repeats=6",
                "--dpi-desync-fooling=badseq",
                "--dpi-desync-badseq-increment=1000",
                $"--dpi-desync-fake-tls={google}"
            },
            "tcp-05-multisplit-seqovl" => new List<string>
            {
                "--dpi-desync=multisplit",
                "--dpi-desync-split-seqovl=681",
                "--dpi-desync-split-pos=1",
                $"--dpi-desync-split-seqovl-pattern={google}"
            },
            "tcp-06-multisplit-sniext" => new List<string>
            {
                "--dpi-desync=multisplit",
                "--dpi-desync-split-pos=2,sniext+1",
                "--dpi-desync-split-seqovl=679",
                $"--dpi-desync-split-seqovl-pattern={google}"
            },
            "tcp-07-fake-hostfakesplit" => new List<string>
            {
                "--dpi-desync=fake,hostfakesplit",
                "--dpi-desync-fake-tls-mod=rnd,dupsid,sni=www.google.com",
                "--dpi-desync-hostfakesplit-mod=host=www.google.com,altorder=1",
                "--dpi-desync-fooling=ts",
                $"--dpi-desync-fake-tls={google}"
            },
            "tcp-08-hostfakesplit" => new List<string>
            {
                "--dpi-desync=hostfakesplit",
                "--dpi-desync-repeats=4",
                "--dpi-desync-fooling=ts",
                "--dpi-desync-hostfakesplit-mod=host=www.google.com"
            },
            "tcp-09-auto-multisplit-badseq" => new List<string>
            {
                "--dpi-desync=fake,multisplit",
                "--dpi-desync-split-seqovl=681",
                "--dpi-desync-split-pos=1",
                "--dpi-desync-fooling=badseq",
                "--dpi-desync-badseq-increment=10000000",
                "--dpi-desync-repeats=8",
                $"--dpi-desync-split-seqovl-pattern={google}",
                "--dpi-desync-fake-tls-mod=rnd,dupsid,sni=www.google.com",
                $"--dpi-desync-fake-tls={google}"
            },
            "tcp-10-auto-multisplit-ts" => new List<string>
            {
                "--dpi-desync=fake,multisplit",
                "--dpi-desync-split-seqovl=681",
                "--dpi-desync-split-pos=1",
                "--dpi-desync-fooling=ts",
                "--dpi-desync-repeats=8",
                $"--dpi-desync-split-seqovl-pattern={google}",
                "--dpi-desync-fake-tls-mod=rnd,dupsid,sni=www.google.com",
                $"--dpi-desync-fake-tls={google}"
            },
            "tcp-11-fake-multidisorder" => new List<string>
            {
                "--dpi-desync=fake,multidisorder",
                "--dpi-desync-split-pos=1,midsld",
                "--dpi-desync-repeats=11",
                "--dpi-desync-fooling=badseq",
                "--dpi-desync-fake-tls-mod=rnd,dupsid,sni=www.google.com",
                $"--dpi-desync-fake-tls={google}"
            },
            "tcp-12-auto-fakedsplit" => new List<string>
            {
                "--dpi-desync=fake,fakedsplit",
                "--dpi-desync-split-pos=1",
                "--dpi-desync-fooling=badseq",
                "--dpi-desync-badseq-increment=2",
                "--dpi-desync-repeats=8",
                "--dpi-desync-fake-tls-mod=rnd,dupsid,sni=www.google.com",
                $"--dpi-desync-fake-tls={google}"
            },
            "tcp-13-multidisorder-sniext" => new List<string>
            {
                "--dpi-desync=multidisorder",
                "--dpi-desync-split-pos=2,sniext+1",
                "--dpi-desync-split-seqovl=679",
                $"--dpi-desync-split-seqovl-pattern={google}"
            },
            "tcp-14-fakedsplit-midsld" => new List<string>
            {
                "--dpi-desync=fakedsplit",
                "--dpi-desync-split-pos=midsld+1",
                "--dpi-desync-fakedsplit-pattern=0x00",
                "--dpi-desync-fooling=ts"
            },
            "tcp-15-multisplit-iana" => new List<string>
            {
                "--dpi-desync=multisplit",
                "--dpi-desync-split-seqovl=517",
                "--dpi-desync-split-pos=1",
                $"--dpi-desync-split-seqovl-pattern={iana}"
            },
            "tcp-16-fake-iana-r11" => new List<string>
            {
                "--dpi-desync=fake",
                "--dpi-desync-repeats=11",
                "--dpi-desync-fooling=ts",
                $"--dpi-desync-fake-tls={iana}"
            },
            "tcp-17-multisplit-seqovl652" => new List<string>
            {
                "--dpi-desync=multisplit",
                "--dpi-desync-split-seqovl=652",
                "--dpi-desync-split-pos=2",
                $"--dpi-desync-split-seqovl-pattern={google}"
            },
            "tcp-18-fake-default-nomod" => new List<string>
            {
                "--dpi-desync=fake",
                "--dpi-desync-repeats=6",
                "--dpi-desync-fooling=badseq",
                "--dpi-desync-badseq-increment=2",
                "--dpi-desync-fake-tls-mod=none"
            },
            "tcp-19-auto-multidisorder" => new List<string>
            {
                "--dpi-desync=fake,multidisorder",
                "--dpi-desync-split-pos=1,midsld",
                "--dpi-desync-repeats=11",
                "--dpi-desync-fooling=badseq",
                "--dpi-desync-fake-tls=0x00000000",
                "--dpi-desync-fake-tls=!",
                "--dpi-desync-fake-tls-mod=rnd,dupsid,sni=www.google.com"
            },
            _ => throw new ArgumentOutOfRangeException(nameof(strategyId), strategyId, "Unknown TCP strategy.")
        };

        if (includeHttp
            && result.Any(argument => argument.StartsWith("--dpi-desync=fake", StringComparison.Ordinal)))
        {
            result.Add($"--dpi-desync-fake-http={LegacyFake("http_iana_org.bin")}");
        }

        return result;
    }

    private IReadOnlyList<string> BuildAdaptiveUdpPlan(
        ISet<ServiceId> selected,
        KrotStartOptions options,
        PresetSelection selection,
        ICollection<string> presetIds)
    {
        var useQuic = selected.Contains(ServiceId.YouTube)
                      && selection.YouTubeQuic != PresetSelection.Direct;
        var useVoice = selected.Contains(ServiceId.Discord)
                       && !options.SkipVoice
                       && selection.DiscordVoice != PresetSelection.Direct;
        if (!useQuic && !useVoice)
        {
            return Array.Empty<string>();
        }

        var result = new List<string>
        {
            "--lua-init=@lua\\zapret-lib.lua",
            "--lua-init=@lua\\zapret-antidpi.lua",
            "--lua-init=@lua\\zapret-auto.lua",
            "--lua-init=@lua\\krot-auto.lua"
        };
        var ports = useQuic && useVoice
            ? "443,19294-19344,50000-50099"
            : useQuic
                ? "443"
                : "19294-19344,50000-50099";
        result.Add($"--wf-udp-out={ports}");
        result.Add($"--wf-udp-in={ports}");

        if (useQuic)
        {
            AddQuicBlobs(result);
        }

        if (useVoice)
        {
            result.Add($"--blob=voice_discovery:@{LegacyFake("discord-ip-discovery-without-port.bin")}");
            result.Add($"--blob=voice_dtls:@{LegacyFake("dtls_clienthello_w3_org.bin")}");
            result.Add($"--wf-raw-part=@{RuntimeFile("filters", "windivert_part.discord_media.txt")}");
            result.Add($"--wf-raw-part=@{RuntimeFile("filters", "windivert_part.discord_media_in.txt")}");
            result.Add($"--wf-raw-part=@{RuntimeFile("filters", "windivert_part.stun.txt")}");
        }

        var firstProfile = true;
        if (useQuic)
        {
            AddAdaptiveProfile(
                result,
                AdaptiveQuicProfile(selection.YouTubeQuic),
                ref firstProfile);
            presetIds.Add("youtube-quic:adaptive-8");
        }

        if (useVoice)
        {
            AddAdaptiveProfile(
                result,
                AdaptiveVoiceProfile(selection.DiscordVoice),
                ref firstProfile);
            presetIds.Add("discord-voice:adaptive-12");
        }

        return result;
    }

    private void AddQuicBlobs(ICollection<string> destination)
    {
        var added = new HashSet<string>(StringComparer.Ordinal);
        foreach (var strategy in BuiltInStrategyCatalog.Quic)
        {
            var definition = QuicDefinition(strategy.Id);
            if (added.Add(definition.BlobName))
            {
                destination.Add(
                    $"--blob={definition.BlobName}:@{LegacyFake(definition.FileName)}");
            }
        }
    }

    private IReadOnlyList<string> AdaptiveQuicProfile(string preferredStrategyId)
    {
        var ordered = PreferredOrder(
                BuiltInStrategyCatalog.Quic,
                preferredStrategyId)
            .ToList();
        var result = new List<string>
        {
            "--filter-udp=443",
            "--filter-l7=quic",
            $"--hostlist={RuntimeFile("hostlists", "youtube.txt")}",
            "--in-range=a",
            "--lua-desync=krot_circular:key=youtube_quic:channel=youtube_quic"
            + $":ids={string.Join(",", ordered.Select(item => item.Id))}"
            + ":fails=1:time=300:udp_out=4:udp_in=1"
        };
        var strategyNumber = 1;
        foreach (var strategy in ordered)
        {
            var definition = QuicDefinition(strategy.Id);
            result.Add(
                $"--lua-desync=fake:blob={definition.BlobName}:repeats={definition.Repeats}"
                + $":payload=quic_initial:strategy={strategyNumber}");
            strategyNumber++;
        }

        return result;
    }

    private static IReadOnlyList<string> AdaptiveVoiceProfile(string preferredStrategyId)
    {
        var ordered = PreferredOrder(
                BuiltInStrategyCatalog.Voice,
                preferredStrategyId)
            .ToList();
        var result = new List<string>
        {
            "--filter-l7=discord,stun",
            "--in-range=a",
            "--lua-desync=krot_circular:key=discord_voice:channel=discord_voice"
            + $":ids={string.Join(",", ordered.Select(item => item.Id))}"
            + ":fails=1:time=300:udp_out=6:udp_in=0"
        };
        var strategyNumber = 1;
        foreach (var strategy in ordered)
        {
            AddVoiceStrategy(result, strategy.Id, strategyNumber);
            strategyNumber++;
        }

        return result;
    }

    private static void AddVoiceStrategy(
        ICollection<string> destination,
        string strategyId,
        int strategyNumber)
    {
        const string zero = "0x00000000000000000000000000000000";
        const string voicePayload = "stun,discord_ip_discovery";
        var tag = $":payload={voicePayload}:strategy={strategyNumber}";
        switch (strategyId)
        {
            case "voice-01-fake-r2":
                destination.Add($"--lua-desync=fake:blob={zero}:repeats=2{tag}");
                break;
            case "voice-02-fake-r6":
                destination.Add($"--lua-desync=fake:blob={zero}:repeats=6{tag}");
                break;
            case "voice-03-fake-r10":
                destination.Add($"--lua-desync=fake:blob={zero}:repeats=10{tag}");
                break;
            case "voice-04-fake-ttl1-r6":
                destination.Add($"--lua-desync=fake:blob={zero}:repeats=6:ip_ttl=1{tag}");
                break;
            case "voice-05-fake-autottl-r6":
                destination.Add(
                    $"--lua-desync=fake:blob={zero}:repeats=6:ip_autottl=-1,3-20{tag}");
                break;
            case "voice-06-udplen-2":
                destination.Add($"--lua-desync=udplen:increment=2{tag}");
                break;
            case "voice-07-udplen-8":
                destination.Add($"--lua-desync=udplen:increment=8{tag}");
                break;
            case "voice-08-fake-r4-udplen2":
                destination.Add($"--lua-desync=fake:blob={zero}:repeats=4{tag}");
                destination.Add($"--lua-desync=udplen:increment=2{tag}");
                break;
            case "voice-09-discovery-blob-r6":
                destination.Add(
                    $"--lua-desync=fake:blob=voice_discovery:repeats=6{tag}");
                break;
            case "voice-10-dtls-blob-r6":
                destination.Add(
                    "--lua-desync=fake:blob=voice_dtls:repeats=6"
                    + $":payload=stun,discord_ip_discovery,dtls_client_hello"
                    + $":strategy={strategyNumber}");
                break;
            case "voice-11-ipfrag":
                destination.Add(
                    $"--lua-desync=send:ipfrag:ipfrag_pos_udp=8{tag}");
                destination.Add($"--lua-desync=drop{tag}");
                break;
            case "voice-12-ipfrag-disorder":
                destination.Add(
                    $"--lua-desync=send:ipfrag:ipfrag_disorder:ipfrag_pos_udp=8{tag}");
                destination.Add($"--lua-desync=drop{tag}");
                break;
            default:
                throw new ArgumentOutOfRangeException(
                    nameof(strategyId),
                    strategyId,
                    "Unknown voice strategy.");
        }
    }

    private static void AddAdaptiveProfile(
        ICollection<string> destination,
        IEnumerable<string> profile,
        ref bool firstProfile)
    {
        if (!firstProfile)
        {
            destination.Add("--new");
        }

        firstProfile = false;
        foreach (var argument in profile)
        {
            destination.Add(argument);
        }
    }

    private static IEnumerable<StrategyDescriptor> PreferredOrder(
        IEnumerable<StrategyDescriptor> strategies,
        string preferredStrategyId) =>
        strategies.OrderBy(strategy =>
            string.Equals(
                strategy.Id,
                preferredStrategyId,
                StringComparison.Ordinal)
                ? 0
                : 1);

    private static QuicStrategyDefinition QuicDefinition(string strategyId) =>
        strategyId switch
        {
            "quic-01-google-r6" =>
                new("quic_google", "quic_initial_www_google_com.bin", 6),
            "quic-02-google-r11" =>
                new("quic_google", "quic_initial_www_google_com.bin", 11),
            "quic-03-facebook-r6" =>
                new("quic_facebook", "quic_initial_facebook_com.bin", 6),
            "quic-04-facebook-quiche-r11" =>
                new("quic_facebook_quiche", "quic_initial_facebook_com_quiche.bin", 11),
            "quic-05-googlevideo-kyber1-r6" =>
                new(
                    "quic_googlevideo_kyber1",
                    "quic_initial_rr1---sn-xguxaxjvh-n8me_googlevideo_com_kyber_1.bin",
                    6),
            "quic-06-googlevideo-kyber2-r10" =>
                new(
                    "quic_googlevideo_kyber2",
                    "quic_initial_rr1---sn-xguxaxjvh-n8me_googlevideo_com_kyber_2.bin",
                    10),
            "quic-07-rutracker-kyber-r8" =>
                new(
                    "quic_rutracker_kyber",
                    "quic_initial_rutracker_org_kyber_1.bin",
                    8),
            "quic-08-vk-r12" =>
                new("quic_vk", "quic_initial_vk_com.bin", 12),
            _ => throw new ArgumentOutOfRangeException(
                nameof(strategyId),
                strategyId,
                "Unknown QUIC strategy.")
        };

    private sealed class QuicStrategyDefinition
    {
        public QuicStrategyDefinition(string blobName, string fileName, int repeats)
        {
            BlobName = blobName;
            FileName = fileName;
            Repeats = repeats;
        }

        public string BlobName { get; }

        public string FileName { get; }

        public int Repeats { get; }
    }

    private static void Validate(PresetSelection selection)
    {
        if (!BuiltInStrategyCatalog.IsTcp(selection.DiscordTcp)
            || !BuiltInStrategyCatalog.IsTcp(selection.YouTubeTcp)
            || !BuiltInStrategyCatalog.IsQuic(selection.YouTubeQuic)
            || !BuiltInStrategyCatalog.IsVoice(selection.DiscordVoice))
        {
            throw new ArgumentException("Preset selection contains an unknown strategy.", nameof(selection));
        }
    }

    private string LegacyFake(string file) =>
        RuntimeFile(Path.Combine(LegacyRuntimeId, "fake"), file);

    private string RuntimeFile(string folder, string file) =>
        Path.GetFullPath(Path.Combine(_runtimeRoot, folder, file));

    private static void AddProfiles(
        ICollection<string> destination,
        IEnumerable<IReadOnlyList<string>> profiles)
    {
        var first = true;
        foreach (var profile in profiles)
        {
            if (!first)
            {
                destination.Add("--new");
            }

            first = false;
            foreach (var argument in profile.Where(x => !string.IsNullOrWhiteSpace(x)))
            {
                destination.Add(argument);
            }
        }
    }
}
