using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using KROT.Zapret.Processes;
using Xunit;

namespace KROT.Zapret.Tests;

public sealed class FakeZapretProcessManagerTests
{
    [Fact]
    public async Task RestartVoice_DoesNotReplaceMain()
    {
        var manager = new FakeZapretProcessManager();
        await manager.StartMainAsync(Array.Empty<string>(), CancellationToken.None);
        await manager.StartVoiceAsync(Array.Empty<string>(), CancellationToken.None);
        var mainPid = manager.OwnedProcesses.Single(x => x.Role == "main").ProcessId;
        var voicePid = manager.OwnedProcesses.Single(x => x.Role == "voice").ProcessId;

        await manager.RestartVoiceAsync(Array.Empty<string>(), CancellationToken.None);

        Assert.Equal(mainPid, manager.OwnedProcesses.Single(x => x.Role == "main").ProcessId);
        Assert.NotEqual(voicePid, manager.OwnedProcesses.Single(x => x.Role == "voice").ProcessId);
    }

    [Fact]
    public async Task RestartMain_DoesNotReplaceVoice()
    {
        var manager = new FakeZapretProcessManager();
        await manager.StartMainAsync(Array.Empty<string>(), CancellationToken.None);
        await manager.StartVoiceAsync(Array.Empty<string>(), CancellationToken.None);
        var mainPid = manager.OwnedProcesses.Single(x => x.Role == "main").ProcessId;
        var voicePid = manager.OwnedProcesses.Single(x => x.Role == "voice").ProcessId;

        await manager.RestartMainAsync(Array.Empty<string>(), CancellationToken.None);

        Assert.NotEqual(mainPid, manager.OwnedProcesses.Single(x => x.Role == "main").ProcessId);
        Assert.Equal(voicePid, manager.OwnedProcesses.Single(x => x.Role == "voice").ProcessId);
    }

    [Fact]
    public async Task Stop_RemovesOnlyRecordedOwnedProcesses()
    {
        var manager = new FakeZapretProcessManager();
        await manager.StartMainAsync(Array.Empty<string>(), CancellationToken.None);

        await manager.StopAllOwnedAsync(CancellationToken.None);

        Assert.Empty(manager.OwnedProcesses);
    }
}
