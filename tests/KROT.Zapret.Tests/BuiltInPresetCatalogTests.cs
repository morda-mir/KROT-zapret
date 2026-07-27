using System.IO;
using KROT.Core.Models;
using KROT.Zapret.Profiles;
using Xunit;

namespace KROT.Zapret.Tests;

public sealed class BuiltInPresetCatalogTests
{
    private readonly BuiltInPresetCatalog _catalog =
        new(Path.Combine(Path.GetTempPath(), "KROT runtime"));

    [Fact]
    public void Build_AiServicesOnly_DoesNotInterceptTraffic()
    {
        var plan = _catalog.Build(new KrotStartOptions
        {
            Services = { ServiceId.AiServices }
        });

        Assert.False(plan.HasMain);
        Assert.False(plan.HasVoice);
    }

    [Fact]
    public void Build_YouTube_UsesLegacyAltProfileAndQuicScope()
    {
        var plan = _catalog.Build(new KrotStartOptions
        {
            Services = { ServiceId.YouTube }
        });

        Assert.Contains("--wf-tcp=80,443", plan.MainArguments);
        Assert.Contains("--wf-udp=443", plan.MainArguments);
        Assert.Contains(plan.MainArguments, x => x.Contains("youtube.txt"));
        Assert.Contains(plan.MainArguments, x => x.Contains("fake,fakedsplit"));
        Assert.Contains(plan.MainArguments, x => x.Contains("zapret1-v72.13"));
        Assert.Contains("z1-youtube-alt-01", plan.PresetIds);
        Assert.False(plan.HasVoice);
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
        Assert.Contains(plan.VoiceArguments, x => x.Contains("stun"));
        Assert.DoesNotContain("--wf-udp=443", plan.VoiceArguments);
        Assert.Contains("z1-discord-alt-01", plan.PresetIds);
        Assert.Contains("z2-discord-voice-01", plan.PresetIds);
    }

}
