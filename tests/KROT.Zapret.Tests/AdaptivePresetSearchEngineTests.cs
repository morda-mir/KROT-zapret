using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using KROT.Core.Contracts;
using KROT.Core.Models;
using KROT.Zapret.Profiles;
using Xunit;

namespace KROT.Zapret.Tests;

public sealed class AdaptivePresetSearchEngineTests
{
    private readonly BuiltInPresetCatalog _catalog =
        new(Path.Combine(Path.GetTempPath(), "KROT runtime"));

    [Fact]
    public async Task DirectAccess_DoesNotStartMainAndCachesDirectSelection()
    {
        var processManager = new RecordingProcessManager();
        var probe = new QueueProbe();
        probe.Add(ServiceId.Discord, true);
        probe.Add(ServiceId.YouTube, true);
        var cache = new MemoryCache();
        var engine = Create(processManager, probe, cache);

        var outcome = await engine.StartMainAsync(
            Options(ServiceId.Discord, ServiceId.YouTube),
            "network-a",
            allowSearch: true,
            stageChanged: null,
            CancellationToken.None);

        Assert.Equal(PresetSelection.Direct, outcome.Selection.DiscordTcp);
        Assert.Equal(PresetSelection.Direct, outcome.Selection.YouTubeTcp);
        Assert.Equal(
            BuiltInStrategyCatalog.DefaultQuicId,
            outcome.Selection.YouTubeQuic);
        Assert.Empty(processManager.OwnedProcesses);
        Assert.NotNull(cache.Value);
    }

    [Fact]
    public async Task Search_SelectsFirstWorkingTcpCandidatePerService()
    {
        var processManager = new RecordingProcessManager();
        var probe = new QueueProbe();
        probe.Add(ServiceId.Discord, false, false, true);
        probe.Add(ServiceId.YouTube, true);
        var cache = new MemoryCache();
        var stages = new List<PresetSearchStage>();
        var engine = Create(processManager, probe, cache);

        var outcome = await engine.StartMainAsync(
            Options(ServiceId.Discord, ServiceId.YouTube),
            "network-b",
            allowSearch: true,
            stages.Add,
            CancellationToken.None);

        Assert.Equal(BuiltInStrategyCatalog.Tcp[1].Id, outcome.Selection.DiscordTcp);
        Assert.Equal(PresetSelection.Direct, outcome.Selection.YouTubeTcp);
        Assert.True(outcome.AllTcpReachable);
        Assert.True(outcome.TcpReachability[ServiceId.Discord]);
        Assert.True(outcome.TcpReachability[ServiceId.YouTube]);
        Assert.Contains(PresetSearchStage.Searching, stages);
        Assert.Equal(1, processManager.StartMainCount);
        Assert.Equal(1, processManager.RestartMainCount);
    }

    [Fact]
    public async Task SavedWinner_IsTestedBeforeCatalogSearch()
    {
        var processManager = new RecordingProcessManager();
        var probe = new QueueProbe();
        probe.Add(ServiceId.Discord, true);
        var cache = new MemoryCache
        {
            Value = new PresetSelection
            {
                DiscordTcp = "tcp-08-hostfakesplit"
            }
        };
        var stages = new List<PresetSearchStage>();
        var engine = Create(processManager, probe, cache);

        var outcome = await engine.StartMainAsync(
            Options(ServiceId.Discord),
            "network-c",
            allowSearch: true,
            stages.Add,
            CancellationToken.None);

        Assert.Equal("tcp-08-hostfakesplit", outcome.Selection.DiscordTcp);
        Assert.DoesNotContain(PresetSearchStage.Searching, stages);
        Assert.Equal(PresetSearchStage.TestingSaved, stages[0]);
        Assert.Equal(1, probe.CallCount);
        Assert.Equal(1, processManager.StartMainCount);
        Assert.Equal(0, processManager.RestartMainCount);
    }

    [Fact]
    public async Task FailedSavedWinner_FallsBackToDirectBeforeCatalog()
    {
        var processManager = new RecordingProcessManager();
        var probe = new QueueProbe();
        probe.Add(ServiceId.Discord, false, true);
        var cache = new MemoryCache
        {
            Value = new PresetSelection
            {
                DiscordTcp = "tcp-08-hostfakesplit"
            }
        };
        var stages = new List<PresetSearchStage>();
        var engine = Create(processManager, probe, cache);

        var outcome = await engine.StartMainAsync(
            Options(ServiceId.Discord),
            "network-d",
            allowSearch: true,
            stages.Add,
            CancellationToken.None);

        Assert.Equal(PresetSelection.Direct, outcome.Selection.DiscordTcp);
        Assert.Equal(
            new[]
            {
                PresetSearchStage.TestingSaved,
                PresetSearchStage.TestingDirect
            },
            stages);
        Assert.DoesNotContain(PresetSearchStage.Searching, stages);
        Assert.Equal(2, probe.CallCount);
        Assert.Empty(processManager.OwnedProcesses);
    }

    [Fact]
    public async Task FailedSavedDirect_IsNotProbedTwice()
    {
        var processManager = new RecordingProcessManager();
        var probe = new QueueProbe();
        probe.Add(ServiceId.Discord, false, true);
        var cache = new MemoryCache
        {
            Value = new PresetSelection
            {
                DiscordTcp = PresetSelection.Direct
            }
        };
        var stages = new List<PresetSearchStage>();
        var engine = Create(processManager, probe, cache);

        var outcome = await engine.StartMainAsync(
            Options(ServiceId.Discord),
            "network-e",
            allowSearch: true,
            stages.Add,
            CancellationToken.None);

        Assert.Equal(BuiltInStrategyCatalog.Tcp[0].Id, outcome.Selection.DiscordTcp);
        Assert.Equal(2, probe.CallCount);
        Assert.DoesNotContain(PresetSearchStage.TestingDirect, stages);
        Assert.Contains(PresetSearchStage.Searching, stages);
    }

    [Fact]
    public async Task VpnMode_SkipsProbesAndUsesDefaultPlan()
    {
        var processManager = new RecordingProcessManager();
        var probe = new QueueProbe();
        var cache = new MemoryCache();
        var engine = Create(processManager, probe, cache);

        var outcome = await engine.StartMainAsync(
            Options(ServiceId.Discord),
            "network-vpn",
            allowSearch: false,
            stageChanged: null,
            CancellationToken.None);

        Assert.Equal(BuiltInStrategyCatalog.DefaultTcpId, outcome.Selection.DiscordTcp);
        Assert.Null(outcome.TcpReachability[ServiceId.Discord]);
        Assert.Equal(0, probe.CallCount);
        Assert.Equal(1, processManager.StartMainCount);
    }

    private AdaptivePresetSearchEngine Create(
        IZapretProcessManager processManager,
        IPresetReachabilityProbe probe,
        IPresetSelectionCache cache) =>
        new(processManager, _catalog, probe, cache, new NullLog());

    private static KrotStartOptions Options(params ServiceId[] services)
    {
        var options = new KrotStartOptions();
        options.Services.AddRange(services);
        return options;
    }

    private sealed class QueueProbe : IPresetReachabilityProbe
    {
        private readonly Dictionary<ServiceId, Queue<bool>> _results = new();

        public int CallCount { get; private set; }

        public void Add(ServiceId serviceId, params bool[] results) =>
            _results[serviceId] = new Queue<bool>(results);

        public Task<bool> CheckAsync(
            ServiceId serviceId,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CallCount++;
            return Task.FromResult(
                _results.TryGetValue(serviceId, out var values) && values.Count > 0
                    ? values.Dequeue()
                    : false);
        }
    }

    private sealed class MemoryCache : IPresetSelectionCache
    {
        public PresetSelection? Value { get; set; }

        public Task<PresetSelection?> LoadAsync(
            string networkFingerprint,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(Value?.Clone());
        }

        public Task SaveAsync(
            string networkFingerprint,
            PresetSelection selection,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Value = selection.Clone();
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingProcessManager : IZapretProcessManager
    {
        private readonly List<RuntimeProcessRecord> _owned = new();
        private int _pid = 50000;

        public int StartMainCount { get; private set; }

        public int RestartMainCount { get; private set; }

        public IReadOnlyCollection<RuntimeProcessRecord> OwnedProcesses => _owned.AsReadOnly();

        public Task StartMainAsync(
            IReadOnlyList<string> arguments,
            CancellationToken cancellationToken)
        {
            StartMainCount++;
            _owned.Add(Record("main"));
            return Task.CompletedTask;
        }

        public Task RestartMainAsync(
            IReadOnlyList<string> arguments,
            CancellationToken cancellationToken)
        {
            RestartMainCount++;
            _owned.RemoveAll(item => item.Role == "main");
            _owned.Add(Record("main"));
            return Task.CompletedTask;
        }

        public Task StopMainAsync(CancellationToken cancellationToken)
        {
            _owned.RemoveAll(item => item.Role == "main");
            return Task.CompletedTask;
        }

        public Task StartVoiceAsync(
            IReadOnlyList<string> arguments,
            CancellationToken cancellationToken)
        {
            _owned.Add(Record("voice"));
            return Task.CompletedTask;
        }

        public Task RestartVoiceAsync(
            IReadOnlyList<string> arguments,
            CancellationToken cancellationToken)
        {
            _owned.RemoveAll(item => item.Role == "voice");
            _owned.Add(Record("voice"));
            return Task.CompletedTask;
        }

        public Task StopAllOwnedAsync(CancellationToken cancellationToken)
        {
            _owned.Clear();
            return Task.CompletedTask;
        }

        private RuntimeProcessRecord Record(string role) => new()
        {
            Role = role,
            ProcessId = Interlocked.Increment(ref _pid),
            StartedUtc = DateTime.UtcNow,
            OwnershipMarker = Guid.NewGuid()
        };
    }

    private sealed class NullLog : ILogService
    {
        public void Info(string eventName, string message)
        {
        }

        public void Error(string eventName, string message, Exception? exception = null)
        {
        }
    }
}
