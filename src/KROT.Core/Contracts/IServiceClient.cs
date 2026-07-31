using System;
using System.Threading;
using System.Threading.Tasks;
using KROT.Core.Models;

namespace KROT.Core.Contracts;

public interface IServiceClient
{
    event EventHandler<ServiceSnapshot>? SnapshotChanged;

    Task<ServiceSnapshot> GetStatusAsync(CancellationToken cancellationToken);

    Task StartAsync(KrotStartOptions options, CancellationToken cancellationToken);

    Task StopAsync(CancellationToken cancellationToken);

    Task SetDetailedLogsAsync(bool enabled, CancellationToken cancellationToken);

    Task RefreshServiceAsync(
        ServiceId serviceId,
        CancellationToken cancellationToken);

    Task ShutdownAsync(CancellationToken cancellationToken);
}
