using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using KROT.Core.Contracts;
using KROT.Core.Models;
using KROT.Core.States;
using KROT.Diagnostics.FieldTesting;
using KROT.Service.Hosting;
using KROT.Zapret.Processes;
using KROT.Zapret.Profiles;
using Xunit;

namespace KROT.Service.Tests;

public sealed class ServiceEngineTests
{
    [Fact]
    public async Task FakeRuntime_StartAndStop_LeavesNoOwnedProcesses()
    {
        var processManager = new FakeZapretProcessManager();
        var log = new RecordingLog();
        var engine = CreateEngine(processManager, log);
        var options = Options(detailedLogs: false);

        await engine.StartAsync(options, CancellationToken.None);

        Assert.Equal(AppState.Running, engine.Snapshot.AppState);
        Assert.NotEmpty(processManager.OwnedProcesses);
        Assert.Contains(log.InfoEvents, item => item == "runtime.starting");
        Assert.False(log.Detailed);

        await engine.StopAsync(CancellationToken.None);

        Assert.Equal(AppState.Off, engine.Snapshot.AppState);
        Assert.Empty(processManager.OwnedProcesses);
        Assert.Contains(log.InfoEvents, item => item == "runtime.stopped");
    }

    [Fact]
    public async Task StopDuringStart_CancelsStartAndWaitsForCleanup()
    {
        var processManager = new BlockingProcessManager();
        var log = new RecordingLog();
        var engine = CreateEngine(processManager, log);
        var startTask = engine.StartAsync(Options(detailedLogs: true), CancellationToken.None);
        await processManager.StartEntered.Task;

        var stopTask = engine.StopAsync(CancellationToken.None);
        var completed = await Task.WhenAny(stopTask, Task.Delay(TimeSpan.FromSeconds(5)));

        Assert.Same(stopTask, completed);
        await stopTask;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await startTask);
        Assert.Equal(AppState.Off, engine.Snapshot.AppState);
        Assert.Empty(processManager.OwnedProcesses);
        Assert.Equal(1, processManager.StopAllCount);
        Assert.True(log.Detailed);
    }

    [Fact]
    public async Task FailedStart_CleansOwnedProcessesAndRecordsBothErrors()
    {
        var processManager = new FailingProcessManager();
        var log = new RecordingLog();
        var engine = CreateEngine(processManager, log);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => engine.StartAsync(Options(detailedLogs: false), CancellationToken.None));

        Assert.Equal(AppState.FatalError, engine.Snapshot.AppState);
        Assert.Empty(processManager.OwnedProcesses);
        Assert.Equal(1, processManager.StopAllCount);
        Assert.Contains(log.ErrorEvents, item => item == "runtime.start.failed");
    }

    [Fact]
    public async Task DetailedSetting_IsAppliedForEveryNewSession()
    {
        var processManager = new FakeZapretProcessManager();
        var log = new RecordingLog();
        var engine = CreateEngine(processManager, log);

        await engine.StartAsync(Options(detailedLogs: true), CancellationToken.None);
        Assert.True(log.Detailed);
        await engine.StopAsync(CancellationToken.None);

        await engine.StartAsync(Options(detailedLogs: false), CancellationToken.None);
        Assert.False(log.Detailed);
        await engine.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task DiscordVoice_TracksActivityWinnerAndFailure()
    {
        var processManager = new OutputProcessManager();
        var log = new RecordingLog();
        var engine = CreateEngine(
            processManager,
            log,
            TimeSpan.FromMilliseconds(40));

        await engine.StartAsync(Options(detailedLogs: true), CancellationToken.None);
        Assert.Equal(ChannelState.WaitingForActivity, VoiceState(engine));

        processManager.Emit("KROT_UDP_ACTIVITY|discord_voice");
        Assert.Equal(ChannelState.Testing, VoiceState(engine));

        processManager.Emit(
            "KROT_UDP_WINNER|discord_voice|voice-01-fake-r2");
        Assert.Equal(ChannelState.WorkingPreset, VoiceState(engine));

        await engine.StopAsync(CancellationToken.None);
        await engine.StartAsync(Options(detailedLogs: true), CancellationToken.None);
        processManager.Emit("KROT_UDP_ACTIVITY|discord_voice");
        Assert.Equal(ChannelState.Testing, VoiceState(engine));

        await Task.Delay(TimeSpan.FromMilliseconds(150));
        Assert.Equal(ChannelState.Failed, VoiceState(engine));
        Assert.Contains(log.InfoEvents, item => item == "preset.udp.failed");

        processManager.Emit("KROT_UDP_ACTIVITY|discord_voice");
        Assert.Equal(ChannelState.Testing, VoiceState(engine));
        await engine.StopAsync(CancellationToken.None);
    }

    private static ServiceEngine CreateEngine(
        IZapretProcessManager processManager,
        ILogService log,
        TimeSpan? udpConfirmationTimeout = null)
    {
        var runtimeRoot = Path.Combine(Path.GetTempPath(), "KROT-service-tests");
        var catalog = new BuiltInPresetCatalog(runtimeRoot);
        var presetSearch = new AdaptivePresetSearchEngine(
            processManager,
            catalog,
            new NeverReachableProbe(),
            new MemoryPresetCache(),
            log);
        return new ServiceEngine(
            processManager,
            catalog,
            presetSearch,
            new NetworkEnvironmentInspector(),
            new AlwaysOnlineProbe(),
            log,
            isFakeRuntime: true,
            udpConfirmationTimeout: udpConfirmationTimeout);
    }

    private static ChannelState VoiceState(ServiceEngine engine) =>
        engine.Snapshot.Channels.Single(item =>
            item.ServiceId == ServiceId.Discord
            && item.ChannelId == "voice").State;

    private static KrotStartOptions Options(bool detailedLogs)
    {
        var options = new KrotStartOptions
        {
            DetailedLogs = detailedLogs
        };
        options.Services.Add(ServiceId.Discord);
        return options;
    }

    private sealed class RecordingLog : IConfigurableLogService
    {
        public List<string> InfoEvents { get; } = new();

        public List<string> ErrorEvents { get; } = new();

        public bool Detailed { get; set; }

        public void Info(string eventName, string message) => InfoEvents.Add(eventName);

        public void Detail(string eventName, string message)
        {
            if (Detailed)
            {
                InfoEvents.Add(eventName);
            }
        }

        public void Error(string eventName, string message, Exception? exception = null) =>
            ErrorEvents.Add(eventName);
    }

    private sealed class AlwaysOnlineProbe : IInternetAvailabilityProbe
    {
        public Task<bool> CheckAsync(CancellationToken cancellationToken) =>
            Task.FromResult(true);
    }

    private sealed class NeverReachableProbe : IPresetReachabilityProbe
    {
        public Task<bool> CheckAsync(
            ServiceId serviceId,
            CancellationToken cancellationToken) =>
            Task.FromResult(false);
    }

    private sealed class MemoryPresetCache : IPresetSelectionCache
    {
        public Task<PresetSelection?> LoadAsync(
            string networkFingerprint,
            CancellationToken cancellationToken) =>
            Task.FromResult<PresetSelection?>(null);

        public Task SaveAsync(
            string networkFingerprint,
            PresetSelection selection,
            CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }

    private sealed class BlockingProcessManager : IZapretProcessManager
    {
        private readonly List<RuntimeProcessRecord> _owned = new();

        public TaskCompletionSource<bool> StartEntered { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int StopAllCount { get; private set; }

        public IReadOnlyCollection<RuntimeProcessRecord> OwnedProcesses =>
            _owned.AsReadOnly();

        public Task StartMainAsync(
            IReadOnlyList<string> arguments,
            CancellationToken cancellationToken) =>
            StartAndBlockAsync(cancellationToken);

        public Task StartVoiceAsync(
            IReadOnlyList<string> arguments,
            CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task RestartMainAsync(
            IReadOnlyList<string> arguments,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task StopMainAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task RestartVoiceAsync(
            IReadOnlyList<string> arguments,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task StopAllOwnedAsync(CancellationToken cancellationToken)
        {
            StopAllCount++;
            _owned.Clear();
            return Task.CompletedTask;
        }

        private async Task StartAndBlockAsync(CancellationToken cancellationToken)
        {
            _owned.Add(Record("main"));
            StartEntered.TrySetResult(true);
            await Task.Delay(Timeout.Infinite, cancellationToken);
        }
    }

    private sealed class FailingProcessManager : IZapretProcessManager
    {
        private readonly List<RuntimeProcessRecord> _owned = new();

        public int StopAllCount { get; private set; }

        public IReadOnlyCollection<RuntimeProcessRecord> OwnedProcesses =>
            _owned.AsReadOnly();

        public Task StartMainAsync(
            IReadOnlyList<string> arguments,
            CancellationToken cancellationToken)
        {
            _owned.Add(Record("main"));
            throw new InvalidOperationException("Synthetic startup failure.");
        }

        public Task StartVoiceAsync(
            IReadOnlyList<string> arguments,
            CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task RestartMainAsync(
            IReadOnlyList<string> arguments,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task StopMainAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task RestartVoiceAsync(
            IReadOnlyList<string> arguments,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task StopAllOwnedAsync(CancellationToken cancellationToken)
        {
            StopAllCount++;
            _owned.Clear();
            return Task.CompletedTask;
        }
    }

    private sealed class OutputProcessManager :
        IZapretProcessManager,
        IRuntimeOutputSource
    {
        private readonly List<RuntimeProcessRecord> _owned = new();

        public event EventHandler<RuntimeOutputEvent>? RuntimeOutput;

        public IReadOnlyCollection<RuntimeProcessRecord> OwnedProcesses =>
            _owned.AsReadOnly();

        public Task StartMainAsync(
            IReadOnlyList<string> arguments,
            CancellationToken cancellationToken)
        {
            _owned.Add(Record("main"));
            return Task.CompletedTask;
        }

        public Task StartVoiceAsync(
            IReadOnlyList<string> arguments,
            CancellationToken cancellationToken)
        {
            _owned.Add(Record("voice"));
            return Task.CompletedTask;
        }

        public Task RestartMainAsync(
            IReadOnlyList<string> arguments,
            CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task StopMainAsync(CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task RestartVoiceAsync(
            IReadOnlyList<string> arguments,
            CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task StopAllOwnedAsync(CancellationToken cancellationToken)
        {
            _owned.Clear();
            return Task.CompletedTask;
        }

        public void Emit(string line) =>
            RuntimeOutput?.Invoke(
                this,
                new RuntimeOutputEvent
                {
                    Role = "voice",
                    Line = line
                });
    }

    private static RuntimeProcessRecord Record(string role) => new()
    {
        Role = role,
        ProcessId = 50001,
        StartedUtc = DateTime.UtcNow,
        OwnershipMarker = Guid.NewGuid()
    };
}
