using System;
using System.IO;
using System.IO.Pipes;
using System.Security.Principal;
using System.ServiceProcess;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using KROT.Core.Contracts;
using KROT.Core.Ipc;
using KROT.Core.Models;
using Newtonsoft.Json;

namespace KROT.App.Services;

public sealed class NamedPipeServiceClient : IServiceClient
{
    private const string ServiceName = "KROTZapret";
    private const int MaxResponseBytes = 1024 * 1024;
    private static readonly TimeSpan ShortResponseTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan StartResponseTimeout = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan ShutdownTimeout = TimeSpan.FromSeconds(12);
    private static readonly TimeSpan ServiceStartTimeout = TimeSpan.FromSeconds(15);
    private static readonly SemaphoreSlim ServiceStartGate = new(1, 1);

    public event EventHandler<ServiceSnapshot>? SnapshotChanged;

    public Task<ServiceSnapshot> GetStatusAsync(CancellationToken cancellationToken) =>
        SendAsync("status", null, null, cancellationToken);

    public async Task StartAsync(KrotStartOptions options, CancellationToken cancellationToken)
    {
        var snapshot = await SendAsync("start", options, null, cancellationToken);
        SnapshotChanged?.Invoke(this, snapshot);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        var snapshot = await SendAsync("stop", null, null, cancellationToken);
        SnapshotChanged?.Invoke(this, snapshot);
    }

    public async Task SetDetailedLogsAsync(
        bool enabled,
        CancellationToken cancellationToken)
    {
        var snapshot = await SendAsync(
            "set-detailed-logs",
            null,
            enabled,
            cancellationToken);
        SnapshotChanged?.Invoke(this, snapshot);
    }

    public async Task ShutdownAsync(CancellationToken cancellationToken)
    {
        if (!ServiceNeedsShutdown())
        {
            return;
        }

        using var timeout =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(ShutdownTimeout);
        await SendAsync("shutdown", null, null, timeout.Token);
    }

    private static async Task<ServiceSnapshot> SendAsync(
        string command,
        KrotStartOptions? options,
        bool? detailedLogs,
        CancellationToken cancellationToken)
    {
        await EnsureServiceRunningAsync(cancellationToken).ConfigureAwait(false);
        var sid = WindowsIdentity.GetCurrent().User?.Value
            ?? throw new InvalidOperationException("Cannot determine current user SID.");
        using var pipe = new NamedPipeClientStream(
            ".",
            ServiceProtocol.PipeNameForSid(sid),
            PipeDirection.InOut,
            PipeOptions.Asynchronous);
        await pipe.ConnectAsync(3000, cancellationToken).ConfigureAwait(false);

        using var writer = new StreamWriter(pipe, new UTF8Encoding(false), 4096, leaveOpen: true)
        {
            AutoFlush = true
        };
        var request = new ServiceRequest
        {
            Command = command,
            StartOptions = options,
            DetailedLogs = detailedLogs
        };
        await writer.WriteLineAsync(JsonConvert.SerializeObject(request)).ConfigureAwait(false);
        var line = await BoundedUtf8LineReader
            .ReadAsync(
                pipe,
                MaxResponseBytes,
                string.Equals(command, "start", StringComparison.Ordinal)
                    ? StartResponseTimeout
                    : ShortResponseTimeout,
                cancellationToken)
            .ConfigureAwait(false);
        var response = JsonConvert.DeserializeObject<ServiceResponse>(line ?? string.Empty)
            ?? throw new InvalidOperationException("Empty response from KROT service.");
        if (!response.Success)
        {
            throw new InvalidOperationException(response.ErrorCode);
        }

        return response.Snapshot;
    }

    private static async Task EnsureServiceRunningAsync(
        CancellationToken cancellationToken)
    {
        await ServiceStartGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            using var controller = new ServiceController(ServiceName);
            var deadline = DateTime.UtcNow + ServiceStartTimeout;
            var startIssued = false;
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                controller.Refresh();
                if (controller.Status == ServiceControllerStatus.Running)
                {
                    return;
                }

                if (controller.Status == ServiceControllerStatus.Stopped
                    && !startIssued)
                {
                    controller.Start();
                    startIssued = true;
                }

                if (DateTime.UtcNow >= deadline)
                {
                    throw new System.TimeoutException(
                        "Timed out while starting the KROT service.");
                }

                await Task
                    .Delay(TimeSpan.FromMilliseconds(200), cancellationToken)
                    .ConfigureAwait(false);
            }
        }
        finally
        {
            ServiceStartGate.Release();
        }
    }

    private static bool ServiceNeedsShutdown()
    {
        try
        {
            using var controller = new ServiceController(ServiceName);
            controller.Refresh();
            return controller.Status is not (
                ServiceControllerStatus.Stopped
                or ServiceControllerStatus.StopPending);
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }
}
