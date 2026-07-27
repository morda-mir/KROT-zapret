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
    public void Build_YouTube_UsesHostlistAndQuicScope()
    {
        var plan = _catalog.Build(new KrotStartOptions
        {
            Services = { ServiceId.YouTube }
        });

        Assert.Contains("--wf-tcp-out=443", plan.MainArguments);
        Assert.Contains("--wf-udp-out=443", plan.MainArguments);
        Assert.Contains(plan.MainArguments, x => x.Contains("youtube.txt"));
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
        Assert.Contains(plan.VoiceArguments, x => x.Contains("discord_media"));
        Assert.Contains(plan.VoiceArguments, x => x.Contains("stun"));
        Assert.DoesNotContain("--wf-udp-out=443", plan.VoiceArguments);
    }
}
