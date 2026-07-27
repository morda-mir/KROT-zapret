using System.Net.NetworkInformation;
using KROT.Diagnostics.FieldTesting;
using Xunit;

namespace KROT.Diagnostics.Tests;

public sealed class NetworkEnvironmentInspectorTests
{
    [Theory]
    [InlineData("WireGuard Tunnel", "Wintun Userspace Tunnel", NetworkInterfaceType.Ethernet)]
    [InlineData("Local", "OpenVPN TAP adapter", NetworkInterfaceType.Ethernet)]
    [InlineData("happ-tun", "sing-tun Tunnel", NetworkInterfaceType.Unknown)]
    [InlineData("Connection", "Regular adapter", NetworkInterfaceType.Tunnel)]
    public void VpnMarkers_AreDetected(
        string name,
        string description,
        NetworkInterfaceType interfaceType)
    {
        Assert.True(NetworkEnvironmentInspector.IsLikelyVpn(name, description, interfaceType));
    }

    [Fact]
    public void OrdinaryEthernet_IsNotClassifiedAsVpn()
    {
        Assert.False(NetworkEnvironmentInspector.IsLikelyVpn(
            "Ethernet",
            "Intel Ethernet Controller",
            NetworkInterfaceType.Ethernet));
    }
}
