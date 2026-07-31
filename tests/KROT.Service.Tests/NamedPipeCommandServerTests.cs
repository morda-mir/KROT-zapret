using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using KROT.Core.Contracts;
using KROT.Core.Ipc;
using KROT.Core.Models;
using KROT.Diagnostics.FieldTesting;
using KROT.Service.Hosting;
using KROT.Service.Ipc;
using KROT.Zapret.Processes;
using KROT.Zapret.Profiles;
using Newtonsoft.Json;
using Xunit;

namespace KROT.Service.Tests;

public sealed class NamedPipeCommandServerTests
{
    [Fact]
    public void ServiceSnapshot_ExternalTunnelFlag_RoundTripsInProtocolJson()
    {
        var response = new ServiceResponse
        {
            Success = true,
            Snapshot = new ServiceSnapshot
            {
                ExternalTunnelDetected = true
            }
        };

        var json = JsonConvert.SerializeObject(response);
        var restored = JsonConvert.DeserializeObject<ServiceResponse>(json);

        Assert.NotNull(restored);
        Assert.True(restored!.Snapshot.ExternalTunnelDetected);
    }

    [Fact]
    public async Task ConcurrentStatusAndStop_WorkWhileStartIsInProgress()
    {
        var pipeName = CreateUniquePipeName();
        var processManager = new BlockingProcessManager();
        var log = new ConfigurableNullLog();
        var server = new NamedPipeCommandServer(
            CreateEngine(processManager, log),
            log,
            pipeName);
        using var serverCancellation = new CancellationTokenSource();
        var serverTask = server.RunAsync(serverCancellation.Token);
        var options = new KrotStartOptions();
        options.Services.Add(ServiceId.Discord);

        var startResponseTask = SendAsync(pipeName, new ServiceRequest
        {
            Command = "start",
            StartOptions = options
        });
        await processManager.StartEntered.Task;

        ServiceResponse statusResponse;
        try
        {
            statusResponse = await SendAsync(
                pipeName,
                new ServiceRequest { Command = "status" });
        }
        catch (Exception exception)
        {
            throw new InvalidOperationException(
                string.Join(Environment.NewLine, log.Messages),
                exception);
        }
        Assert.True(statusResponse.Success);
        Assert.NotEqual(KROT.Core.States.AppState.Off, statusResponse.Snapshot.AppState);

        var loggingResponse = await SendAsync(pipeName, new ServiceRequest
        {
            Command = "set-detailed-logs",
            DetailedLogs = true
        });
        Assert.True(loggingResponse.Success);
        Assert.True(log.Detailed);

        var stopResponse = await SendAsync(
            pipeName,
            new ServiceRequest { Command = "stop" });
        var startResponse = await startResponseTask;
        Assert.True(stopResponse.Success);
        Assert.True(startResponse.Success);
        Assert.Equal(KROT.Core.States.AppState.Off, stopResponse.Snapshot.AppState);
        Assert.Empty(processManager.OwnedProcesses);

        serverCancellation.Cancel();
        await serverTask;
        Assert.False(server.HasActiveClients);
    }

    [Fact]
    public async Task Cancellation_WaitsForConnectedClientToExit()
    {
        var pipeName = CreateUniquePipeName();
        var server = new NamedPipeCommandServer(
            CreateEngine(new FakeZapretProcessManager(), new NullLog()),
            new NullLog(),
            pipeName);
        using var serverCancellation = new CancellationTokenSource();
        var serverTask = server.RunAsync(serverCancellation.Token);
        using var client = CreateClient(pipeName);
        await client.ConnectAsync(5000, CancellationToken.None);
        await WaitUntilAsync(() => server.HasActiveClients);

        serverCancellation.Cancel();
        var completed = await Task.WhenAny(
            serverTask,
            Task.Delay(TimeSpan.FromSeconds(5)));

        Assert.Same(serverTask, completed);
        await serverTask;
        Assert.False(server.HasActiveClients);
    }

    private static async Task<ServiceResponse> SendAsync(
        string pipeName,
        ServiceRequest request)
    {
        using var client = CreateClient(pipeName);
        await client.ConnectAsync(5000, CancellationToken.None);
        using var writer = new StreamWriter(
            client,
            new UTF8Encoding(false),
            4096,
            leaveOpen: true)
        {
            AutoFlush = true
        };
        using var reader = new StreamReader(
            client,
            new UTF8Encoding(false, true),
            detectEncodingFromByteOrderMarks: false,
            bufferSize: 4096,
            leaveOpen: true);
        await writer.WriteLineAsync(JsonConvert.SerializeObject(request));
        var responseLine = await reader.ReadLineAsync();
        return JsonConvert.DeserializeObject<ServiceResponse>(responseLine!)
            ?? throw new InvalidDataException("Empty service response.");
    }

    private static NamedPipeClientStream CreateClient(string pipeName) =>
        new(
            ".",
            pipeName,
            PipeDirection.InOut,
            PipeOptions.Asynchronous);

    private static string CreateUniquePipeName() =>
        $"KROT.Service.Tests.{Guid.NewGuid():N}";

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (!condition())
        {
            if (DateTime.UtcNow >= deadline)
            {
                throw new TimeoutException("Condition was not reached.");
            }

            await Task.Delay(20);
        }
    }

    private static ServiceEngine CreateEngine(
        IZapretProcessManager processManager,
        ILogService log)
    {
        var catalog = new BuiltInPresetCatalog(
            Path.Combine(Path.GetTempPath(), "KROT-service-tests"));
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
            isFakeRuntime: true);
    }

    private sealed class NullLog : ILogService
    {
        public void Info(string eventName, string message)
        {
        }

        public void Detail(string eventName, string message)
        {
        }

        public void Error(string eventName, string message, Exception? exception = null)
        {
        }
    }

    private sealed class ConfigurableNullLog : IConfigurableLogService
    {
        public List<string> Messages { get; } = new();

        public bool Detailed { get; set; }

        public void Info(string eventName, string message) =>
            Messages.Add($"{eventName}: {message}");

        public void Detail(string eventName, string message) =>
            Messages.Add($"{eventName}: {message}");

        public void Error(string eventName, string message, Exception? exception = null) =>
            Messages.Add($"{eventName}: {message} {exception}");
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

        public IReadOnlyCollection<RuntimeProcessRecord> OwnedProcesses =>
            _owned.AsReadOnly();

        public async Task StartMainAsync(
            IReadOnlyList<string> arguments,
            CancellationToken cancellationToken)
        {
            _owned.Add(new RuntimeProcessRecord
            {
                Role = "main",
                ProcessId = 50002,
                StartedUtc = DateTime.UtcNow,
                OwnershipMarker = Guid.NewGuid()
            });
            StartEntered.TrySetResult(true);
            await Task.Delay(Timeout.Infinite, cancellationToken);
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
            _owned.Clear();
            return Task.CompletedTask;
        }
    }
}
