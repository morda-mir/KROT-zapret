using System.Threading;
using System.Threading.Tasks;
using KROT.Core.Contracts;

namespace KROT.Zapret.Processes;

public sealed class FakeTelegramProxyManager : ITelegramProxyManager
{
    public bool IsRunning { get; private set; }

    public Task StartAsync(string secret, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IsRunning = true;
        return Task.CompletedTask;
    }

    public async Task RestartAsync(string secret, CancellationToken cancellationToken)
    {
        await StopAsync(cancellationToken).ConfigureAwait(false);
        await StartAsync(secret, cancellationToken).ConfigureAwait(false);
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IsRunning = false;
        return Task.CompletedTask;
    }
}
