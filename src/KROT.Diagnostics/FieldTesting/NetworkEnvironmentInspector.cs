using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Security.Cryptography;
using System.Text;

namespace KROT.Diagnostics.FieldTesting;

public sealed class NetworkEnvironmentInspector
{
    private static readonly string[] VpnMarkers =
    {
        "vpn",
        "-tun",
        "tun ",
        " tunnel",
        "sing-box",
        "sing-tun",
        "happ-tun",
        "wireguard",
        "wintun",
        "openvpn",
        "tap-windows",
        "tailscale",
        "zerotier",
        "proton",
        "nord",
        "amnezia",
        "outline",
        "clash"
    };

    public NetworkEnvironmentSummary Inspect()
    {
        var active = NetworkInterface.GetAllNetworkInterfaces()
            .Where(adapter => adapter.OperationalStatus == OperationalStatus.Up)
            .Where(adapter => adapter.NetworkInterfaceType != NetworkInterfaceType.Loopback)
            .ToArray();

        var summaries = active.Select(CreateSummary).ToList();
        var fingerprintSource = string.Join(
            "|",
            active.Select(NormalizeAdapter).OrderBy(value => value, StringComparer.Ordinal));
        var proxyDetected = IsSystemProxyConfigured();

        return new NetworkEnvironmentSummary
        {
            ActiveAdapterCount = active.Length,
            VpnLikely = summaries.Any(adapter => adapter.LikelyVpn) || proxyDetected,
            SystemProxyDetected = proxyDetected,
            FingerprintSha256 = Sha256(fingerprintSource),
            Adapters = summaries
        };
    }

    public bool IsLikelyVpn(NetworkInterface adapter)
    {
        return IsLikelyVpn(adapter.Name, adapter.Description, adapter.NetworkInterfaceType);
    }

    public static bool IsLikelyVpn(
        string name,
        string description,
        NetworkInterfaceType interfaceType)
    {
        if (interfaceType is NetworkInterfaceType.Tunnel or NetworkInterfaceType.Ppp)
        {
            return true;
        }

        var searchable = $"{name} {description}".ToLowerInvariant();
        return VpnMarkers.Any(searchable.Contains);
    }

    private AdapterSummary CreateSummary(NetworkInterface adapter)
    {
        var properties = adapter.GetIPProperties();
        return new AdapterSummary
        {
            IdSha256 = Sha256(adapter.Id),
            InterfaceType = adapter.NetworkInterfaceType.ToString(),
            HasDefaultGateway = properties.GatewayAddresses.Any(
                gateway => !gateway.Address.Equals(IPAddress.Any)
                           && !gateway.Address.Equals(IPAddress.IPv6Any)),
            LikelyVpn = IsLikelyVpn(adapter),
            DnsServerCount = properties.DnsAddresses.Count
        };
    }

    private static string NormalizeAdapter(NetworkInterface adapter)
    {
        var properties = adapter.GetIPProperties();
        var dns = properties.DnsAddresses
            .Select(address => address.ToString())
            .OrderBy(value => value, StringComparer.Ordinal);
        return string.Join(
            ";",
            adapter.Id,
            adapter.NetworkInterfaceType,
            string.Join(",", dns),
            properties.GatewayAddresses.Count > 0 ? "gateway" : "no-gateway");
    }

    private static bool IsSystemProxyConfigured()
    {
        try
        {
            var destination = new Uri("https://example.com/");
            var proxy = WebRequest.DefaultWebProxy?.GetProxy(destination);
            return proxy != null && proxy != destination;
        }
        catch
        {
            return false;
        }
    }

    private static string Sha256(string value)
    {
        using var sha256 = SHA256.Create();
        var hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(value));
        return string.Concat(hash.Select(item => item.ToString("x2")));
    }
}
