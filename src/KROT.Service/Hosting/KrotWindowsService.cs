using System.ServiceProcess;
using System.Threading;
using KROT.Core.Contracts;
using KROT.Service.Ipc;

namespace KROT.Service.Hosting;

public sealed class KrotWindowsService : ServiceBase
{
    private readonly ServiceEngine _engine;
    private readonly ILogService _log;
    private CancellationTokenSource? _cancellation;

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
        _ = server.RunAsync(_cancellation.Token);
        _log.Info("service.start", "KROT service started in idle state.");
    }

    protected override void OnStop()
    {
        _cancellation?.Cancel();
        _engine.StopAsync(CancellationToken.None).GetAwaiter().GetResult();
        _cancellation?.Dispose();
        _log.Info("service.stop", "KROT service stopped.");
    }

    protected override void OnShutdown() => OnStop();
}

