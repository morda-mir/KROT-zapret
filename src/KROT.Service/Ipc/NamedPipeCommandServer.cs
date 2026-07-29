using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using KROT.Core.Contracts;
using KROT.Core.Ipc;
using KROT.Core.Models;
using KROT.Service.Hosting;
using Newtonsoft.Json;

namespace KROT.Service.Ipc;

public sealed class NamedPipeCommandServer
{
    private static readonly TimeSpan RequestReadTimeout = TimeSpan.FromSeconds(5);
    private const int MaxRequestBytes = 16 * 1024;
    private const int MaxPipeInstances = 4;
    private readonly ServiceEngine _engine;
    private readonly ILogService _log;
    private readonly string? _pipeNameOverride;
    private readonly object _clientTasksSync = new();
    private readonly HashSet<Task> _clientTasks = new();
    private long _lastRequestUtcTicks = DateTime.UtcNow.Ticks;
    private int _activeClientCount;
    private readonly JsonSerializerSettings _serializerSettings = new()
    {
        MaxDepth = 16,
        MissingMemberHandling = MissingMemberHandling.Error
    };

    public NamedPipeCommandServer(
        ServiceEngine engine,
        ILogService log,
        string? pipeNameOverride = null)
    {
        _engine = engine;
        _log = log;
        _pipeNameOverride = pipeNameOverride;
    }

    public event EventHandler? ShutdownRequested;

    public DateTime LastRequestUtc =>
        new(Interlocked.Read(ref _lastRequestUtcTicks), DateTimeKind.Utc);

    public bool HasActiveClients => Volatile.Read(ref _activeClientCount) > 0;

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                NamedPipeServerStream? pipe = null;
                var userSid = InteractiveUserSidProvider.TryGetActiveUserSid();
                if (userSid == null)
                {
                    await Task.Delay(1000, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                try
                {
                    pipe = CreatePipe(userSid, _pipeNameOverride);
                    using var registration = cancellationToken.Register(pipe.Dispose);
                    await Task.Factory.FromAsync(
                        (callback, state) => pipe.BeginWaitForConnection(callback, state),
                        pipe.EndWaitForConnection,
                        null).ConfigureAwait(false);
                    var connectedPipe = pipe;
                    pipe = null;
                    TrackClient(HandleClientSafelyAsync(connectedPipe, cancellationToken));
                }
                catch (ObjectDisposedException) when (cancellationToken.IsCancellationRequested)
                {
                    return;
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception ex)
                {
                    _log.Error("pipe.failure", "Named Pipe request failed.", ex);
                    await Task.Delay(250, cancellationToken).ConfigureAwait(false);
                }
                finally
                {
                    pipe?.Dispose();
                }
            }
        }
        finally
        {
            await WaitForClientsAsync().ConfigureAwait(false);
        }
    }

    private async Task HandleClientSafelyAsync(
        NamedPipeServerStream pipe,
        CancellationToken cancellationToken)
    {
        using (pipe)
        using (cancellationToken.Register(pipe.Dispose))
        {
            Interlocked.Increment(ref _activeClientCount);
            try
            {
                await HandleClientAsync(pipe, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
            }
            catch (ObjectDisposedException) when (cancellationToken.IsCancellationRequested)
            {
            }
            catch (Exception ex)
            {
                _log.Error("pipe.client.failed", "Named Pipe client failed.", ex);
            }
            finally
            {
                Interlocked.Decrement(ref _activeClientCount);
            }
        }
    }

    private async Task HandleClientAsync(NamedPipeServerStream pipe, CancellationToken cancellationToken)
    {
        using var writer = new StreamWriter(pipe, new UTF8Encoding(false), 4096, leaveOpen: true)
        {
            AutoFlush = true
        };

        var response = new ServiceResponse();
        var shutdownRequested = false;
        try
        {
            var line = await BoundedUtf8LineReader
                .ReadAsync(
                    pipe,
                    MaxRequestBytes,
                    RequestReadTimeout,
                    cancellationToken)
                .ConfigureAwait(false);
            var request = JsonConvert.DeserializeObject<ServiceRequest>(
                    line,
                    _serializerSettings)
                ?? throw new InvalidOperationException("Empty IPC request.");
            if (request?.ProtocolVersion != ServiceProtocol.Version)
            {
                throw new InvalidOperationException("Unsupported IPC protocol version.");
            }

            if (request.Command is not (
                    "status"
                    or "start"
                    or "stop"
                    or "set-detailed-logs"
                    or "shutdown"))
            {
                throw new InvalidOperationException("Unknown IPC command.");
            }

            Interlocked.Exchange(ref _lastRequestUtcTicks, DateTime.UtcNow.Ticks);
            if (!string.Equals(request.Command, "status", StringComparison.Ordinal))
            {
                _log.Detail(
                    "pipe.command",
                    $"Received IPC command '{request.Command}'.");
            }

            switch (request.Command)
            {
                case "status":
                    break;
                case "start":
                    var options = NormalizeStartOptions(
                        request.StartOptions ?? new KrotStartOptions());
                    await _engine.StartAsync(
                        options,
                        cancellationToken).ConfigureAwait(false);
                    break;
                case "stop":
                    await _engine.StopAsync(cancellationToken).ConfigureAwait(false);
                    break;
                case "set-detailed-logs":
                    if (request.DetailedLogs == null
                        || _log is not IConfigurableLogService configurableLog)
                    {
                        throw new InvalidOperationException(
                            "Detailed logging cannot be configured.");
                    }

                    configurableLog.Detailed = request.DetailedLogs.Value;
                    _log.Info(
                        "logging.detail.changed",
                        $"Detailed service logging enabled={request.DetailedLogs.Value}.");
                    break;
                case "shutdown":
                    await _engine.StopAsync(CancellationToken.None).ConfigureAwait(false);
                    shutdownRequested = true;
                    break;
            }

            response.Success = true;
            response.Snapshot = _engine.Snapshot;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            response.Success = true;
            response.Snapshot = _engine.Snapshot;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            response.Success = false;
            response.ErrorCode = "SERVICE_COMMAND_FAILED";
            response.Snapshot = _engine.Snapshot;
            _log.Error("pipe.command.failed", "Service command failed.", ex);
        }

        await writer.WriteLineAsync(JsonConvert.SerializeObject(response)).ConfigureAwait(false);
        if (shutdownRequested && response.Success)
        {
            ShutdownRequested?.Invoke(this, EventArgs.Empty);
        }
    }

    private void TrackClient(Task task)
    {
        lock (_clientTasksSync)
        {
            _clientTasks.Add(task);
        }

        _ = task.ContinueWith(
            completedTask =>
            {
                lock (_clientTasksSync)
                {
                    _clientTasks.Remove(completedTask);
                }
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private async Task WaitForClientsAsync()
    {
        Task[] clients;
        lock (_clientTasksSync)
        {
            clients = _clientTasks.ToArray();
        }

        if (clients.Length > 0)
        {
            await Task.WhenAll(clients).ConfigureAwait(false);
        }
    }

    private static KrotStartOptions NormalizeStartOptions(KrotStartOptions options)
    {
        options.Services = (options.Services ?? new List<ServiceId>())
            .FindAll(service =>
                service is ServiceId.Discord or ServiceId.YouTube);
        options.Services = new List<ServiceId>(
            new HashSet<ServiceId>(options.Services));
        if (options.Services.Count == 0)
        {
            throw new InvalidOperationException(
                "At least one supported service must be selected.");
        }

        return options;
    }

    private static NamedPipeServerStream CreatePipe(
        SecurityIdentifier userSid,
        string? pipeNameOverride)
    {
        var security = new PipeSecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        security.AddAccessRule(new PipeAccessRule(
            userSid,
            PipeAccessRights.ReadWrite,
            AccessControlType.Allow));
        security.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null),
            PipeAccessRights.FullControl,
            AccessControlType.Allow));
        security.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
            PipeAccessRights.FullControl,
            AccessControlType.Allow));

        return new NamedPipeServerStream(
            pipeNameOverride ?? ServiceProtocol.PipeNameForSid(userSid.Value),
            PipeDirection.InOut,
            MaxPipeInstances,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous,
            4096,
            4096,
            security);
    }
}
