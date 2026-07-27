using System.Linq;
using KROT.Zapret.Arguments;
using Xunit;

namespace KROT.Zapret.Tests;

public sealed class ZapretArgumentBuilderTests
{
    [Fact]
    public void Build_SeparatesProfilesAndRejectsLineBreaks()
    {
        var builder = new ZapretArgumentBuilder();
        var result = builder.Build(
            new[]
            {
                new ZapretProfileArguments { Id = "one", Arguments = new[] { "--filter-tcp=443", "--dpi-desync=fake" } },
                new ZapretProfileArguments { Id = "two", Arguments = new[] { "--filter-udp=443", "bad\r\nargument" } }
            },
            new[] { "outbound and tcp.DstPort == 443" });

        Assert.Contains("--new", result);
        Assert.DoesNotContain(result, value => value.Contains("\r"));
        Assert.Equal(1, result.Count(value => value == "--new"));
    }
}

