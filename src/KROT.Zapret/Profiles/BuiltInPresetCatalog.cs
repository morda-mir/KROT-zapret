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

    public ZapretRuntimePlan Build(KrotStartOptions options)
    {
        var selected = new HashSet<ServiceId>(options.Services);
        var main = new List<string>();
        var profiles = new List<IReadOnlyList<string>>();
        var presetIds = new List<string>();

        if (selected.Contains(ServiceId.Discord))
        {
            profiles.Add(DiscordMediaProfile());
            profiles.Add(TlsProfile("discord.txt", includeHttp: true));
            presetIds.Add("z1-discord-alt-01");
        }

        if (selected.Contains(ServiceId.YouTube))
        {
            profiles.Add(YouTubeTlsProfile());
            profiles.Add(QuicProfile("youtube.txt"));
            presetIds.Add("z1-youtube-alt-01");
        }

        if (profiles.Count > 0)
        {
            var tcpPorts = selected.Contains(ServiceId.Discord)
                ? "80,443,2053,2083,2087,2096,8443"
                : "80,443";
            main.Add($"--wf-tcp={tcpPorts}");
            if (selected.Contains(ServiceId.YouTube))
            {
                main.Add("--wf-udp=443");
            }

            AddProfiles(main, profiles);
        }

        var voice = new List<string>();
        if (selected.Contains(ServiceId.Discord) && !options.SkipVoice)
        {
            voice.Add("--lua-init=@lua\\zapret-lib.lua");
            voice.Add("--lua-init=@lua\\zapret-antidpi.lua");
            voice.Add($"--wf-raw-part=@{RuntimeFile("filters", "windivert_part.discord_media.txt")}");
            voice.Add($"--wf-raw-part=@{RuntimeFile("filters", "windivert_part.stun.txt")}");
            voice.Add("--filter-l7=stun,discord");
            voice.Add("--payload=stun,discord_ip_discovery");
            voice.Add("--lua-desync=fake:blob=0x00000000000000000000000000000000:repeats=2");
            presetIds.Add("z2-discord-voice-01");
        }

        return new ZapretRuntimePlan
        {
            MainArguments = main,
            VoiceArguments = voice,
            PresetIds = presetIds
        };
    }

    private IReadOnlyList<string> DiscordMediaProfile() => new[]
    {
        "--filter-tcp=2053,2083,2087,2096,8443",
        "--hostlist-domains=discord.media",
        "--dpi-desync=fake,fakedsplit",
        "--dpi-desync-repeats=6",
        "--dpi-desync-fooling=ts",
        "--dpi-desync-fakedsplit-pattern=0x00",
        $"--dpi-desync-fake-tls={LegacyFake("tls_clienthello_www_google_com.bin")}"
    };

    private IReadOnlyList<string> YouTubeTlsProfile() => new[]
    {
        "--filter-tcp=443",
        $"--hostlist={RuntimeFile("hostlists", "youtube.txt")}",
        "--ip-id=zero",
        "--dpi-desync=fake,fakedsplit",
        "--dpi-desync-repeats=6",
        "--dpi-desync-fooling=ts",
        "--dpi-desync-fakedsplit-pattern=0x00",
        $"--dpi-desync-fake-tls={LegacyFake("tls_clienthello_www_google_com.bin")}"
    };

    private IReadOnlyList<string> TlsProfile(string hostlist, bool includeHttp)
    {
        var result = new List<string>
        {
            "--filter-tcp=80,443",
            $"--hostlist={RuntimeFile("hostlists", hostlist)}",
            "--dpi-desync=fake,fakedsplit",
            "--dpi-desync-repeats=6",
            "--dpi-desync-fooling=ts",
            "--dpi-desync-fakedsplit-pattern=0x00",
            $"--dpi-desync-fake-tls={LegacyFake("stun.bin")}",
            $"--dpi-desync-fake-tls={LegacyFake("tls_clienthello_www_google_com.bin")}"
        };
        if (includeHttp)
        {
            result.Add($"--dpi-desync-fake-http={LegacyFake("http_iana_org.bin")}");
        }

        return result;
    }

    private IReadOnlyList<string> QuicProfile(string hostlist) => new[]
    {
        "--filter-udp=443",
        $"--hostlist={RuntimeFile("hostlists", hostlist)}",
        "--dpi-desync=fake",
        "--dpi-desync-repeats=6",
        $"--dpi-desync-fake-quic={LegacyFake("quic_initial_www_google_com.bin")}"
    };

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
