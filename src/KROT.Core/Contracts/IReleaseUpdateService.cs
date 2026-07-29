using System.Threading;
using System.Threading.Tasks;
using KROT.Core.Models;

namespace KROT.Core.Contracts;

public interface IReleaseUpdateService
{
    Task<ReleaseUpdateInfo?> CheckForUpdateAsync(
        string currentVersion,
        CancellationToken cancellationToken);
}
