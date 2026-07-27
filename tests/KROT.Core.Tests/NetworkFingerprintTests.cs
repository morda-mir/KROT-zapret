using KROT.Core.Models;
using Xunit;

namespace KROT.Core.Tests;

public sealed class NetworkFingerprintTests
{
    [Fact]
    public void Normalization_IsStableAcrossDnsOrderAndMacFormatting()
    {
        var first = new NetworkFingerprint
        {
            AdapterId = "adapter",
            ConnectionType = "Ethernet",
            GatewayMac = "AA-BB-CC-DD-EE-FF",
            DnsServers = new[] { "8.8.8.8", "1.1.1.1" },
            HasIpv4 = true
        };
        var second = new NetworkFingerprint
        {
            AdapterId = "ADAPTER",
            ConnectionType = "ethernet",
            GatewayMac = "aa:bb:cc:dd:ee:ff",
            DnsServers = new[] { "1.1.1.1", "8.8.8.8" },
            HasIpv4 = true
        };

        Assert.Equal(first.ToNormalizedString(), second.ToNormalizedString());
    }
}

