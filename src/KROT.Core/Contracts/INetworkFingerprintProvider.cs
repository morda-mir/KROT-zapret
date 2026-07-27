using System.Threading;
using System.Threading.Tasks;
using KROT.Core.Models;

namespace KROT.Core.Contracts;

public interface INetworkFingerprintProvider
{
    Task<NetworkFingerprint> GetCurrentAsync(CancellationToken cancellationToken);

    string ComputeId(NetworkFingerprint fingerprint);
}

