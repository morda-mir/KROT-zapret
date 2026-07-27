using System.Threading;
using System.Threading.Tasks;
using KROT.Core.Models;

namespace KROT.Core.Contracts;

public interface ISettingsStore
{
    Task<KrotSettings> LoadAsync(CancellationToken cancellationToken);

    Task SaveAsync(KrotSettings settings, CancellationToken cancellationToken);
}

