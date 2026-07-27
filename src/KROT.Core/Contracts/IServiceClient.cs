using System;
using System.Threading;
using System.Threading.Tasks;
using KROT.Core.Models;

namespace KROT.Core.Contracts;

public interface IServiceClient
{
    event EventHandler<ServiceSnapshot>? SnapshotChanged;

    Task<ServiceSnapshot> GetStatusAsync(CancellationToken cancellationToken);

    Task StartAsync(CancellationToken cancellationToken);

    Task StopAsync(CancellationToken cancellationToken);
}

