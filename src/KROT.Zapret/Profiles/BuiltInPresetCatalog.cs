using System.Collections.Generic;
using System.IO;
using System.Linq;
using KROT.Core.Models;

namespace KROT.Zapret.Profiles;

public sealed class BuiltInPresetCatalog
{
    private readonly string _runtimeRoot;

    public BuiltInPresetCatalog(string runtimeRoot)
    {
        _runtimeRoot = runtimeRoot;
    }

    public ZapretRuntimePlan Build(KrotStartOptions options)
    {
        var selected = new HashSet<ServiceId>(options.Services);
        var main = new List<string>();
        var presetIds = new List<string>();
        var profiles = new List<IReadOnlyList<string>>();

        foreach (var serviceId in selected)
        {
            switch (serviceId)
            {
                case ServiceId.Discord:
                    profiles.Add(TlsProfile("discord.txt"));
                    presetIds.Add("z2-discord-tls-rnd-01");
                    break;
                case ServiceId.YouTube:
                    profiles.Add(TlsProfile("youtube.txt"));
                    profiles.Add(QuicProfile("youtube.txt"));
                    presetIds.Add("z2-youtube-tls-quic-01");
                    break;
                case ServiceId.Telegram:
                    profiles.Add(TlsProfile("telegram.txt"));
                    presetIds.Add("z2-telegram-tls-rnd-01");
                    break;
                case ServiceId.AiServices:
                    // Direct diagnostics show this channel is reachable; do not intercept it.
                    break;
            }
        }

        if (profiles.Count > 0)
        {
            main.Add("--lua-init=@lua\\zapret-lib.lua");
            main.Add("--lua-init=@lua\\zapret-antidpi.lua");
            main.Add("--blob=quic_google:@fake\\quic_initial_www_google_com.bin");
            main.Add("--wf-tcp-out=443");
            if (selected.Contains(ServiceId.YouTube))
            {
                main.Add("--wf-udp-out=443");
            }

            AddProfiles(main, profiles);
        }

        var voice = new List<string>();
        if (selected.Contains(ServiceId.Discord))
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

    private IReadOnlyList<string> TlsProfile(string hostlist) => new[]
    {
        "--filter-tcp=443",
        "--filter-l7=tls",
        $"--hostlist={RuntimeFile("hostlists", hostlist)}",
        "--out-range=-d10",
        "--payload=tls_client_hello",
        "--lua-desync=fake:blob=fake_default_tls:tcp_md5:tcp_seq=-10000:repeats=6",
        "--lua-desync=multidisorder:pos=midsld"
    };

    private IReadOnlyList<string> QuicProfile(string hostlist) => new[]
    {
        "--filter-udp=443",
        "--filter-l7=quic",
        $"--hostlist={RuntimeFile("hostlists", hostlist)}",
        "--payload=quic_initial",
        "--lua-desync=fake:blob=quic_google:repeats=11"
    };

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
