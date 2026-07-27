using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using KROT.Core.Models;

namespace KROT.Core.Contracts;

public interface IZapretProcessManager
{
    IReadOnlyCollection<RuntimeProcessRecord> OwnedProcesses { get; }

    Task StartMainAsync(IReadOnlyList<string> arguments, CancellationToken cancellationToken);

    Task StartVoiceAsync(IReadOnlyList<string> arguments, CancellationToken cancellationToken);

    Task RestartVoiceAsync(IReadOnlyList<string> arguments, CancellationToken cancellationToken);

    Task StopAllOwnedAsync(CancellationToken cancellationToken);
}

