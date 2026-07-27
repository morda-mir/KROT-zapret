using System;
using System.Collections.Generic;
using System.Linq;

namespace KROT.Core.Models;

public sealed class NetworkFingerprint
{
    public string AdapterId { get; set; } = string.Empty;

    public string ConnectionType { get; set; } = string.Empty;

    public string GatewayMac { get; set; } = string.Empty;

    public IReadOnlyList<string> DnsServers { get; set; } = Array.Empty<string>();

    public bool HasIpv4 { get; set; }

    public bool HasIpv6 { get; set; }

    public string ToNormalizedString() =>
        string.Join("|",
            AdapterId.Trim().ToUpperInvariant(),
            ConnectionType.Trim().ToUpperInvariant(),
            GatewayMac.Replace("-", string.Empty).Replace(":", string.Empty).ToUpperInvariant(),
            string.Join(",", DnsServers.OrderBy(x => x, StringComparer.OrdinalIgnoreCase)),
            HasIpv4 ? "4" : "-",
            HasIpv6 ? "6" : "-");
}

