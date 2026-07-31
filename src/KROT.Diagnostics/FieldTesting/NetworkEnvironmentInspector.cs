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
        var activeVpnAdapters = summaries
            .Where(adapter => adapter.LikelyVpn)
            .Select(adapter => $"{adapter.Name} ({adapter.Description})")
            .ToArray();
        var reasons = new List<string>();
        if (activeVpnAdapters.Length > 0)
        {
            reasons.Add($"routed VPN adapter(s): {string.Join(", ", activeVpnAdapters)}");
        }

        if (proxyDetected)
        {
            reasons.Add("system proxy");
        }

        return new NetworkEnvironmentSummary
        {
            ActiveAdapterCount = active.Length,
            VpnLikely = activeVpnAdapters.Length > 0 || proxyDetected,
            SystemProxyDetected = proxyDetected,
            FingerprintSha256 = Sha256(fingerprintSource),
            Adapters = summaries,
            DetectionReason = string.Join("; ", reasons)
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

    public static bool IsActiveVpn(
        string name,
        string description,
        NetworkInterfaceType interfaceType,
        bool hasDefaultGateway) =>
        hasDefaultGateway && IsLikelyVpn(name, description, interfaceType);

    private AdapterSummary CreateSummary(NetworkInterface adapter)
    {
        var properties = adapter.GetIPProperties();
        var hasDefaultGateway = properties.GatewayAddresses.Any(
            gateway => !gateway.Address.Equals(IPAddress.Any)
                       && !gateway.Address.Equals(IPAddress.IPv6Any));
        var markerMatched = IsLikelyVpn(adapter);
        return new AdapterSummary
        {
            IdSha256 = Sha256(adapter.Id),
            InterfaceType = adapter.NetworkInterfaceType.ToString(),
            HasDefaultGateway = hasDefaultGateway,
            LikelyVpn = markerMatched && hasDefaultGateway,
            VpnMarkerMatched = markerMatched,
            DnsServerCount = properties.DnsAddresses.Count,
            Name = adapter.Name,
            Description = adapter.Description
        };
    }

    private static string NormalizeAdapter(NetworkInterface adapter)
    {
        var properties = adapter.GetIPProperties();
        var dns = properties.DnsAddresses
            .Select(address => address.ToString())
            .OrderBy(value => value, StringComparer.Ordinal);
        var gateways = properties.GatewayAddresses
            .Select(gateway => gateway.Address)
            .Where(address =>
                !address.Equals(IPAddress.Any)
                && !address.Equals(IPAddress.IPv6Any))
            .Select(address => address.ToString())
            .OrderBy(value => value, StringComparer.Ordinal);
        var dhcpServers = properties.DhcpServerAddresses
            .Select(address => address.ToString())
            .OrderBy(value => value, StringComparer.Ordinal);
        var networkPrefixes = properties.UnicastAddresses
            .Select(NormalizeNetworkPrefix)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.Ordinal);
        return string.Join(
            ";",
            adapter.Id,
            adapter.NetworkInterfaceType,
            string.Join(",", dns),
            string.Join(",", gateways),
            string.Join(",", dhcpServers),
            string.Join(",", networkPrefixes));
    }

    private static string NormalizeNetworkPrefix(
        UnicastIPAddressInformation unicast)
    {
        var address = unicast.Address;
        var addressBytes = address.GetAddressBytes();
        if (address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
        {
            try
            {
                var maskBytes = unicast.IPv4Mask.GetAddressBytes();
                if (maskBytes.Length != addressBytes.Length)
                {
                    return string.Empty;
                }

                var networkBytes = new byte[addressBytes.Length];
                var prefixLength = 0;
                for (var index = 0; index < addressBytes.Length; index++)
                {
                    networkBytes[index] = (byte)(addressBytes[index] & maskBytes[index]);
                    prefixLength += CountBits(maskBytes[index]);
                }

                return $"{new IPAddress(networkBytes)}/{prefixLength}";
            }
            catch (NetworkInformationException)
            {
                return string.Empty;
            }
        }

        if (address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
        {
            var prefixBytes = addressBytes.Take(8).ToArray();
            return string.Concat(prefixBytes.Select(value => value.ToString("x2"))) + "/64";
        }

        return string.Empty;
    }

    private static int CountBits(byte value)
    {
        var count = 0;
        while (value != 0)
        {
            count += value & 1;
            value >>= 1;
        }

        return count;
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
