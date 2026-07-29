using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using KROT.Core.Models;
using KROT.Zapret.Profiles;
using KROT.Zapret.Runtime;
using Newtonsoft.Json;
using Xunit;

namespace KROT.Zapret.Tests;

public sealed class BuiltInPresetCatalogTests
{
    private readonly BuiltInPresetCatalog _catalog =
        new(Path.Combine(Path.GetTempPath(), "KROT runtime"));

    [Fact]
    public void StrategyCatalog_HasExpectedCuratedCounts()
    {
        Assert.Equal(16, BuiltInStrategyCatalog.Tcp.Count);
        Assert.Equal(8, BuiltInStrategyCatalog.Quic.Count);
        Assert.Equal(10, BuiltInStrategyCatalog.Voice.Count);
        Assert.Equal(5, BuiltInStrategyCatalog.Tcp.Count(item => item.Fast));
        Assert.Equal(3, BuiltInStrategyCatalog.Quic.Count(item => item.Fast));
        Assert.Equal(4, BuiltInStrategyCatalog.Voice.Count(item => item.Fast));
    }

    [Fact]
    public void Build_NoServices_DoesNotInterceptTraffic()
    {
        var plan = _catalog.Build(new KrotStartOptions());

        Assert.False(plan.HasMain);
        Assert.False(plan.HasVoice);
    }

    [Fact]
    public void Build_YouTube_UsesLegacyTcpAndAdaptiveQuic()
    {
        var plan = _catalog.Build(new KrotStartOptions
        {
            Services = { ServiceId.YouTube }
        });

        Assert.Contains("--wf-tcp=80,443", plan.MainArguments);
        Assert.DoesNotContain("--wf-udp=443", plan.MainArguments);
        Assert.Contains(plan.MainArguments, x => x.Contains("youtube.txt"));
        Assert.Contains(plan.MainArguments, x => x.Contains("fake,fakedsplit"));
        Assert.Contains(plan.MainArguments, x => x.Contains("zapret1-v72.13"));
        Assert.True(plan.HasVoice);
        Assert.Contains("--wf-udp-out=443", plan.VoiceArguments);
        Assert.Contains("--wf-udp-in=443", plan.VoiceArguments);
        Assert.Contains(
            "--lua-init=@lua\\zapret-auto.lua",
            plan.VoiceArguments);
        Assert.Contains(
            "--lua-init=@lua\\krot-auto.lua",
            plan.VoiceArguments);
        Assert.Contains(
            plan.VoiceArguments,
            argument => argument.StartsWith("--lua-desync=krot_circular:key=youtube_quic")
                        && argument.Contains(":ids=quic-01-google-r6,"));
        Assert.Equal(
            8,
            plan.VoiceArguments.Count(argument =>
                argument.StartsWith("--lua-desync=fake:")
                && argument.Contains(":strategy=")));
        Assert.Contains(
            $"youtube-tcp:{BuiltInStrategyCatalog.DefaultTcpId}",
            plan.PresetIds);
        Assert.Contains("youtube-quic:adaptive-8", plan.PresetIds);
    }

    [Fact]
    public void Build_Discord_HasSeparatePreciseVoiceProcess()
    {
        var plan = _catalog.Build(new KrotStartOptions
        {
            Services = { ServiceId.Discord }
        });

        Assert.True(plan.HasMain);
        Assert.True(plan.HasVoice);
        Assert.Contains("--wf-tcp=80,443,2053,2083,2087,2096,8443", plan.MainArguments);
        Assert.Contains(plan.MainArguments, x => x.Contains("discord.txt"));
        Assert.Contains(plan.VoiceArguments, x => x.Contains("discord_media"));
        Assert.Contains(plan.VoiceArguments, x => x.Contains("discord_media_in"));
        Assert.Contains(plan.VoiceArguments, x => x.Contains("stun"));
        Assert.Contains(
            plan.VoiceArguments,
            argument => argument.StartsWith("--wf-udp-out=19294-19344"));
        Assert.Contains(
            plan.VoiceArguments,
            argument => argument.StartsWith("--wf-udp-in=19294-19344"));
        Assert.Contains(
            plan.VoiceArguments,
            argument => argument.StartsWith("--lua-desync=krot_circular:key=discord_voice")
                        && argument.Contains(":ids=voice-01-fake-r2,")
                        && argument.EndsWith(":udp_in=0"));
        Assert.Contains("--filter-l7=discord,stun", plan.VoiceArguments);
        Assert.Contains("--in-range=a", plan.VoiceArguments);
        Assert.DoesNotContain("--filter-udp=19294-19344,50000-50099", plan.VoiceArguments);
        for (var strategy = 1; strategy <= 10; strategy++)
        {
            Assert.Contains(
                plan.VoiceArguments,
                argument => argument.EndsWith($":strategy={strategy}"));
        }
        Assert.Contains(
            $"discord-tcp:{BuiltInStrategyCatalog.DefaultTcpId}",
            plan.PresetIds);
        Assert.Contains("discord-voice:adaptive-10", plan.PresetIds);
    }

    [Fact]
    public void Build_AllStrategies_ProducesScopedPlans()
    {
        foreach (var tcp in BuiltInStrategyCatalog.Tcp)
        {
            var plan = _catalog.Build(
                new KrotStartOptions { Services = { ServiceId.Discord, ServiceId.YouTube } },
                new PresetSelection
                {
                    DiscordTcp = tcp.Id,
                    YouTubeTcp = tcp.Id,
                    YouTubeQuic = PresetSelection.Direct,
                    DiscordVoice = PresetSelection.Direct
                });

            Assert.True(plan.HasMain);
            Assert.Contains(plan.MainArguments, argument => argument.StartsWith("--dpi-desync="));
        }

        foreach (var quic in BuiltInStrategyCatalog.Quic)
        {
            var plan = _catalog.Build(
                new KrotStartOptions { Services = { ServiceId.YouTube } },
                new PresetSelection
                {
                    YouTubeTcp = PresetSelection.Direct,
                    YouTubeQuic = quic.Id
                });

            Assert.False(plan.HasMain);
            Assert.True(plan.HasVoice);
            Assert.Contains("--filter-udp=443", plan.VoiceArguments);
            Assert.Contains(
                plan.VoiceArguments,
                argument => argument.StartsWith("--lua-desync=krot_circular:key=youtube_quic"));
        }

        foreach (var voice in BuiltInStrategyCatalog.Voice)
        {
            var plan = _catalog.Build(
                new KrotStartOptions { Services = { ServiceId.Discord } },
                new PresetSelection
                {
                    DiscordTcp = PresetSelection.Direct,
                    DiscordVoice = voice.Id
                });

            Assert.True(plan.HasVoice);
            Assert.Contains(
                plan.VoiceArguments,
                argument => argument.StartsWith("--lua-desync=krot_circular:key=discord_voice")
                            && argument.EndsWith(":udp_in=0"));
        }
    }

    [Fact]
    public void RuntimeManifest_CoversEveryRootedPresetInput()
    {
        var runtimeRoot = FindRuntimeRoot();
        var catalog = new BuiltInPresetCatalog(runtimeRoot);
        var arguments = new List<string>();
        foreach (var tcp in BuiltInStrategyCatalog.Tcp)
        {
            var plan = catalog.Build(
                new KrotStartOptions
                {
                    Services = { ServiceId.Discord, ServiceId.YouTube }
                },
                new PresetSelection
                {
                    DiscordTcp = tcp.Id,
                    YouTubeTcp = tcp.Id,
                    YouTubeQuic = PresetSelection.Direct,
                    DiscordVoice = PresetSelection.Direct
                });
            arguments.AddRange(plan.MainArguments);
        }

        var adaptivePlan = catalog.Build(new KrotStartOptions
        {
            Services = { ServiceId.Discord, ServiceId.YouTube }
        });
        arguments.AddRange(adaptivePlan.MainArguments);
        arguments.AddRange(adaptivePlan.VoiceArguments);

        var manifest = JsonConvert.DeserializeObject<RuntimeManifest>(
                           File.ReadAllText(
                               Path.Combine(runtimeRoot, "runtime-manifest.json")))
                       ?? throw new InvalidDataException("Runtime manifest is invalid.");
        var entries = manifest.Files.ToDictionary(
            entry => Path.GetFullPath(
                Path.Combine(runtimeRoot, entry.RelativePath)),
            entry => entry,
            StringComparer.OrdinalIgnoreCase);
        var verifier = new FileIntegrityVerifier();
        foreach (var path in RootedInputPaths(arguments))
        {
            Assert.True(
                entries.TryGetValue(path, out var entry),
                $"Preset input is missing from runtime-manifest.json: {path}");
            Assert.True(
                verifier.Verify(path, entry!.Sha256),
                $"Preset input hash is invalid: {path}");
        }
    }

    [Theory]
    [InlineData("KROT_UDP_WINNER|youtube_quic|quic-03-facebook-r6", "youtube_quic", "quic-03-facebook-r6")]
    [InlineData("KROT_UDP_WINNER|discord_voice|voice-06-udplen-2", "discord_voice", "voice-06-udplen-2")]
    public void UdpWinnerMarker_ParsesOnlyKnownStrategies(
        string line,
        string expectedChannel,
        string expectedStrategy)
    {
        Assert.True(UdpWinnerMarkerParser.TryParse(line, out var marker));
        Assert.Equal(expectedChannel, marker.Channel);
        Assert.Equal(expectedStrategy, marker.StrategyId);
        Assert.False(UdpWinnerMarkerParser.TryParse(
            "KROT_UDP_WINNER|youtube_quic|unknown",
            out _));
    }

    [Theory]
    [InlineData("KROT_UDP_ACTIVITY|discord_voice", "discord_voice")]
    [InlineData("KROT_UDP_ACTIVITY|youtube_quic", "youtube_quic")]
    public void UdpActivityMarker_ParsesOnlyKnownChannels(
        string line,
        string expectedChannel)
    {
        Assert.True(UdpActivityMarkerParser.TryParse(line, out var marker));
        Assert.Equal(expectedChannel, marker.Channel);
        Assert.False(UdpActivityMarkerParser.TryParse(
            "KROT_UDP_ACTIVITY|unknown",
            out _));
        Assert.False(UdpActivityMarkerParser.TryParse(
            "KROT_UDP_ACTIVITY|discord_voice|extra",
            out _));
    }

    private static IEnumerable<string> RootedInputPaths(
        IEnumerable<string> arguments)
    {
        foreach (var argument in arguments)
        {
            var markerIndex = argument.LastIndexOf('@');
            var valueIndex = markerIndex >= 0
                ? markerIndex
                : argument.IndexOf('=');
            if (valueIndex < 0 || valueIndex == argument.Length - 1)
            {
                continue;
            }

            var candidate = argument.Substring(valueIndex + 1);
            if (Path.IsPathRooted(candidate))
            {
                yield return Path.GetFullPath(candidate);
            }
        }
    }

    private static string FindRuntimeRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            var candidate = Path.Combine(
                directory.FullName,
                "third_party",
                "zapret");
            if (File.Exists(Path.Combine(candidate, "runtime-manifest.json")))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException(
            "Cannot locate third_party/zapret from the test output.");
    }
}
