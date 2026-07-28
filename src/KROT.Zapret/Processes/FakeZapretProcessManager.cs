using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using KROT.Core.Contracts;
using KROT.Core.Models;

namespace KROT.Zapret.Processes;

public sealed class FakeZapretProcessManager : IZapretProcessManager
{
    private readonly List<RuntimeProcessRecord> _owned = new();
    private int _nextFakePid = 40000;

    public IReadOnlyCollection<RuntimeProcessRecord> OwnedProcesses => _owned.AsReadOnly();

    public Task StartMainAsync(IReadOnlyList<string> arguments, CancellationToken cancellationToken) =>
        StartAsync("main", cancellationToken);

    public Task StartVoiceAsync(IReadOnlyList<string> arguments, CancellationToken cancellationToken) =>
        StartAsync("voice", cancellationToken);

    public async Task RestartMainAsync(
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _owned.RemoveAll(x => string.Equals(x.Role, "main", StringComparison.OrdinalIgnoreCase));
        await StartMainAsync(arguments, cancellationToken).ConfigureAwait(false);
    }

    public Task StopMainAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _owned.RemoveAll(x => string.Equals(
            x.Role,
            "main",
            StringComparison.OrdinalIgnoreCase));
        return Task.CompletedTask;
    }

    public async Task RestartVoiceAsync(IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _owned.RemoveAll(x => string.Equals(x.Role, "voice", StringComparison.OrdinalIgnoreCase));
        await StartVoiceAsync(arguments, cancellationToken).ConfigureAwait(false);
    }

    public Task StopAllOwnedAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _owned.Clear();
        return Task.CompletedTask;
    }

    private Task StartAsync(string role, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_owned.Any(x => string.Equals(x.Role, role, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException($"KROT fake process '{role}' is already running.");
        }

        _owned.Add(new RuntimeProcessRecord
        {
            Role = role,
            ProcessId = Interlocked.Increment(ref _nextFakePid),
            StartedUtc = DateTime.UtcNow,
            ExecutableSha256 = "FAKE",
            OwnershipMarker = Guid.NewGuid()
        });
        return Task.CompletedTask;
    }
}
