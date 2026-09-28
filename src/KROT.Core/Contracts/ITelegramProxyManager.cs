using System.Threading;
using System.Threading.Tasks;

namespace KROT.Core.Contracts;

public interface ITelegramProxyManager
{
    bool IsRunning { get; }

    Task StartAsync(string secret, CancellationToken cancellationToken);

    Task RestartAsync(string secret, CancellationToken cancellationToken);

    Task StopAsync(CancellationToken cancellationToken);
}
