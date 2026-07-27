using System;
using System.Threading;
using System.Threading.Tasks;
using KROT.Core.Contracts;
using KROT.Core.States;

namespace KROT.Diagnostics.Fake;

public sealed class FakeServiceDiagnostic : IServiceDiagnostic, IChannelDiagnostic
{
    private readonly TimeSpan _delay;

    public FakeServiceDiagnostic(TimeSpan? delay = null)
    {
        _delay = delay ?? TimeSpan.FromMilliseconds(150);
    }

    public async Task<ServiceState> CheckAsync(CancellationToken cancellationToken)
    {
        await Task.Delay(_delay, cancellationToken).ConfigureAwait(false);
        return ServiceState.Direct;
    }

    public async Task<ChannelState> CheckAsync(string channelId, CancellationToken cancellationToken)
    {
        await Task.Delay(_delay, cancellationToken).ConfigureAwait(false);
        return ChannelState.WorkingDirect;
    }
}

