using System;
using System.ServiceProcess;
using System.Threading;
using KROT.Infrastructure.Logging;
using KROT.Service.Hosting;
using KROT.Service.Ipc;
using KROT.Zapret.Processes;

namespace KROT.Service;

internal static class Program
{
    private static void Main()
    {
        var log = new RotatingFileLogService(detailed: true);
        var engine = new ServiceEngine(new FakeZapretProcessManager(), log);

        if (Environment.UserInteractive)
        {
            using var cancellation = new CancellationTokenSource();
            Console.CancelKeyPress += (_, args) =>
            {
                args.Cancel = true;
                cancellation.Cancel();
            };

            var server = new NamedPipeCommandServer(engine, log);
            server.RunAsync(cancellation.Token).GetAwaiter().GetResult();
            return;
        }

        ServiceBase.Run(new KrotWindowsService(engine, log));
    }
}

