using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace KROT.Diagnostics.FieldTesting;

public sealed class FieldTestReportWriter
{
    public async Task WriteAsync(
        FieldTestReport report,
        string path,
        CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(path)
            ?? throw new InvalidOperationException("Report path has no directory.");
        Directory.CreateDirectory(directory);
        var temporaryPath = path + ".tmp";
        var json = JsonConvert.SerializeObject(report, Formatting.Indented);

        using (var stream = new FileStream(
                   temporaryPath,
                   FileMode.Create,
                   FileAccess.Write,
                   FileShare.None,
                   4096,
                   useAsync: true))
        using (var writer = new StreamWriter(stream, new UTF8Encoding(false)))
        {
            await writer.WriteAsync(json).ConfigureAwait(false);
            await writer.FlushAsync().ConfigureAwait(false);
            stream.Flush(flushToDisk: true);
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (File.Exists(path))
        {
            File.Replace(temporaryPath, path, destinationBackupFileName: null);
        }
        else
        {
            File.Move(temporaryPath, path);
        }
    }
}

