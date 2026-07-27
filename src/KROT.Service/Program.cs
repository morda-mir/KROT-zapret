using System;
using System.ServiceProcess;
using System.Threading;
using KROT.Infrastructure.Logging;
using KROT.Infrastructure.Storage;
using KROT.Service.Hosting;
using KROT.Service.Ipc;
using KROT.Zapret.Processes;
using KROT.Zapret.Profiles;

namespace KROT.Service;

internal static class Program
{
    private static void Main(string[] args)
    {
        var useRealRuntime = Array.Exists(
            args,
            x => string.Equals(x, "--real-runtime", StringComparison.OrdinalIgnoreCase));
        var runtimeRoot = ReadOption(args, "--runtime-root")
            ?? System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "runtime");
        var log = new RotatingFileLogService(
            detailed: true,
            directory: AppPaths.ServiceLogsDirectory);
        var processManager = useRealRuntime
            ? (KROT.Core.Contracts.IZapretProcessManager)new RealZapretProcessManager(runtimeRoot, log)
            : new FakeZapretProcessManager();
        var engine = new ServiceEngine(
            processManager,
            new BuiltInPresetCatalog(runtimeRoot),
            log,
            isFakeRuntime: !useRealRuntime);

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

    private static string? ReadOption(string[] args, string name)
    {
        for (var index = 0; index < args.Length - 1; index++)
        {
            if (string.Equals(args[index], name, StringComparison.OrdinalIgnoreCase))
            {
                return args[index + 1];
            }
        }

        return null;
    }
}
