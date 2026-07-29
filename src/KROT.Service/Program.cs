using System;
using System.ServiceProcess;
using System.Threading;
using KROT.Diagnostics.FieldTesting;
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
        var useFakeRuntime = Array.Exists(
            args,
            x => string.Equals(x, "--fake-runtime", StringComparison.OrdinalIgnoreCase));
        var useRealRuntime = !useFakeRuntime;
        var runtimeRoot = ReadOption(args, "--runtime-root")
            ?? System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "runtime");
        var log = new RotatingFileLogService(
            detailed: false,
            directory: AppPaths.ServiceLogsDirectory);
        var processManager = useRealRuntime
            ? (KROT.Core.Contracts.IZapretProcessManager)new RealZapretProcessManager(runtimeRoot, log)
            : new FakeZapretProcessManager();
        var presetCatalog = new BuiltInPresetCatalog(runtimeRoot);
        var presetSearch = new AdaptivePresetSearchEngine(
            processManager,
            presetCatalog,
            new HttpPresetReachabilityProbe(log),
            new PresetSelectionCacheStore(AppPaths.PresetCacheFile),
            log);
        var engine = new ServiceEngine(
            processManager,
            presetCatalog,
            presetSearch,
            new NetworkEnvironmentInspector(),
            new InternetAvailabilityProbe(log),
            log,
            isFakeRuntime: !useRealRuntime);

        try
        {
            if (Environment.UserInteractive)
            {
                using var cancellation = new CancellationTokenSource();
                Console.CancelKeyPress += (_, args) =>
                {
                    args.Cancel = true;
                    cancellation.Cancel();
                };

                var server = new NamedPipeCommandServer(engine, log);
                server.ShutdownRequested += (_, _) => cancellation.Cancel();
                try
                {
                    server.RunAsync(cancellation.Token).GetAwaiter().GetResult();
                }
                finally
                {
                    engine.StopAsync(CancellationToken.None).GetAwaiter().GetResult();
                }

                return;
            }

            ServiceBase.Run(new KrotWindowsService(engine, log));
        }
        finally
        {
            (processManager as IDisposable)?.Dispose();
        }
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
