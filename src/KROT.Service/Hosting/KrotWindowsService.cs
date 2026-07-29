using System;
using System.ServiceProcess;
using System.Threading;
using System.Threading.Tasks;
using KROT.Core.Contracts;
using KROT.Service.Ipc;

namespace KROT.Service.Hosting;

public sealed class KrotWindowsService : ServiceBase
{
    private static readonly TimeSpan ClientIdleTimeout = TimeSpan.FromSeconds(15);
    private readonly ServiceEngine _engine;
    private readonly ILogService _log;
    private CancellationTokenSource? _cancellation;
    private Task? _serverTask;
    private Task? _watchdogTask;

    public KrotWindowsService(ServiceEngine engine, ILogService log)
    {
        _engine = engine;
        _log = log;
        ServiceName = "KROTZapret";
        CanStop = true;
        CanShutdown = true;
        AutoLog = false;
    }

    protected override void OnStart(string[] args)
    {
        _cancellation = new CancellationTokenSource();
        var server = new NamedPipeCommandServer(_engine, _log);
        server.ShutdownRequested += OnShutdownRequested;
        _serverTask = RunServerAsync(server, _cancellation.Token);
        _watchdogTask = MonitorClientActivityAsync(server, _cancellation.Token);
        _log.Info("service.start", "KROT service started in idle state.");
    }

    protected override void OnStop()
    {
        var cancellation = Interlocked.Exchange(ref _cancellation, null);
        var serverTask = Interlocked.Exchange(ref _serverTask, null);
        var watchdogTask = Interlocked.Exchange(ref _watchdogTask, null);
        cancellation?.Cancel();
        WaitSafely(serverTask, "service.pipe.stop.failed");
        WaitSafely(watchdogTask, "service.watchdog.stop.failed");
        try
        {
            _engine.StopAsync(CancellationToken.None).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            _log.Error(
                "service.runtime.stop.failed",
                "Failed to stop the runtime while the Windows service was stopping.",
                ex);
        }
        finally
        {
            cancellation?.Dispose();
        }

        _log.Info("service.stop", "KROT service stopped.");
    }

    protected override void OnShutdown() => OnStop();

    private async Task RunServerAsync(
        NamedPipeCommandServer server,
        CancellationToken cancellationToken)
    {
        try
        {
            await server.RunAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _log.Error("service.pipe.failed", "KROT IPC server stopped unexpectedly.", ex);
        }
    }

    private async Task MonitorClientActivityAsync(
        NamedPipeCommandServer server,
        CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await Task
                    .Delay(TimeSpan.FromSeconds(3), cancellationToken)
                    .ConfigureAwait(false);
                if (server.HasActiveClients
                    || DateTime.UtcNow - server.LastRequestUtc < ClientIdleTimeout)
                {
                    continue;
                }

                _log.Info(
                    "service.client.timeout",
                    "No GUI heartbeat was received; stopping the runtime service.");
                _ = Task.Run(RequestStopSafely);
                return;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private void OnShutdownRequested(object? sender, EventArgs eventArgs) =>
        _ = Task.Run(RequestStopSafely);

    private void WaitSafely(Task? task, string eventName)
    {
        if (task == null)
        {
            return;
        }

        try
        {
            task.GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            _log.Error(eventName, "A service background task failed during shutdown.", ex);
        }
    }

    private void RequestStopSafely()
    {
        try
        {
            Stop();
        }
        catch (Exception ex)
        {
            _log.Error(
                "service.stop.request.failed",
                "Failed to stop the KROT Windows service.",
                ex);
        }
    }
}
