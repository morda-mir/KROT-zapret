using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using KROT.Diagnostics.FieldTesting;

namespace KROT.FieldTest;

public partial class App
{
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        if (e.Args.Any(argument => string.Equals(argument, "--headless", StringComparison.OrdinalIgnoreCase)))
        {
            await RunHeadlessAsync(e.Args);
            return;
        }

        var window = new MainWindow();
        MainWindow = window;
        window.Show();
    }

    private async Task RunHeadlessAsync(string[] args)
    {
        try
        {
            var output = GetArgumentValue(args, "--output") ?? FieldTestPaths.CreateReportPath();
            var report = await new FieldTestRunner().RunAsync(progress: null, CancellationToken.None);
            await new FieldTestReportWriter().WriteAsync(report, output, CancellationToken.None);
            Shutdown(0);
        }
        catch
        {
            Shutdown(1);
        }
    }

    private static string? GetArgumentValue(string[] args, string name)
    {
        for (var index = 0; index < args.Length - 1; index++)
        {
            if (string.Equals(args[index], name, StringComparison.OrdinalIgnoreCase))
            {
                return Path.GetFullPath(args[index + 1]);
            }
        }

        return null;
    }
}

