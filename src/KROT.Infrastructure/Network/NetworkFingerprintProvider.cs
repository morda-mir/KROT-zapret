using System;
using System.Linq;
using System.Net.NetworkInformation;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using KROT.Core.Contracts;
using KROT.Core.Models;

namespace KROT.Infrastructure.Network;

public sealed class NetworkFingerprintProvider : INetworkFingerprintProvider
{
    public Task<NetworkFingerprint> GetCurrentAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var adapter = NetworkInterface.GetAllNetworkInterfaces()
            .Where(x => x.OperationalStatus == OperationalStatus.Up)
            .Where(x => x.NetworkInterfaceType != NetworkInterfaceType.Loopback)
            .OrderByDescending(x => x.Speed)
            .FirstOrDefault();

        if (adapter == null)
        {
            return Task.FromResult(new NetworkFingerprint());
        }

        var properties = adapter.GetIPProperties();
        var addresses = properties.UnicastAddresses.Select(x => x.Address).ToArray();
        var fingerprint = new NetworkFingerprint
        {
            AdapterId = adapter.Id,
            ConnectionType = adapter.NetworkInterfaceType.ToString(),
            Gateways = properties.GatewayAddresses
                .Select(x => x.Address.ToString())
                .OrderBy(x => x, StringComparer.Ordinal)
                .ToArray(),
            DnsServers = properties.DnsAddresses.Select(x => x.ToString()).ToArray(),
            HasIpv4 = addresses.Any(x => x.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork),
            HasIpv6 = addresses.Any(x => x.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
        };

        return Task.FromResult(fingerprint);
    }

    public string ComputeId(NetworkFingerprint fingerprint)
    {
        using var sha256 = SHA256.Create();
        var bytes = Encoding.UTF8.GetBytes(fingerprint.ToNormalizedString());
        return string.Concat(sha256.ComputeHash(bytes).Select(x => x.ToString("x2")));
    }
}
