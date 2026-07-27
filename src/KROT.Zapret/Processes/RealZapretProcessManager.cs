using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using KROT.Core.Contracts;
using KROT.Core.Models;
using KROT.Zapret.Runtime;
using Newtonsoft.Json;

namespace KROT.Zapret.Processes;

public sealed class RealZapretProcessManager : IZapretProcessManager, IDisposable
{
    private readonly object _sync = new();
    private readonly List<RuntimeProcessRecord> _owned = new();
    private readonly string _runtimeRoot;
    private readonly ILogService _log;
    private readonly WindowsJobObject _job = new();
    private readonly FileIntegrityVerifier _integrityVerifier = new();
    private bool _disposed;

    public RealZapretProcessManager(string runtimeRoot, ILogService log)
    {
        _runtimeRoot = Path.GetFullPath(runtimeRoot);
        _log = log;
    }

    public IReadOnlyCollection<RuntimeProcessRecord> OwnedProcesses
    {
        get
        {
            lock (_sync)
            {
                return _owned.Select(Clone).ToList().AsReadOnly();
            }
        }
    }

    public Task StartMainAsync(
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken) =>
        StartAsync("main", arguments, cancellationToken);

    public Task StartVoiceAsync(
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken) =>
        StartAsync("voice", arguments, cancellationToken);

    public async Task RestartVoiceAsync(
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        await StopRoleAsync("voice", cancellationToken).ConfigureAwait(false);
        await StartVoiceAsync(arguments, cancellationToken).ConfigureAwait(false);
    }

    public async Task StopAllOwnedAsync(CancellationToken cancellationToken)
    {
        RuntimeProcessRecord[] records;
        lock (_sync)
        {
            records = _owned.ToArray();
        }

        foreach (var record in records)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await StopRecordAsync(record).ConfigureAwait(false);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _job.Dispose();
        lock (_sync)
        {
            _owned.Clear();
        }
    }

    private async Task StartAsync(
        string role,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfDisposed();
        ValidateArguments(arguments);

        lock (_sync)
        {
            if (_owned.Any(x => string.Equals(x.Role, role, StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidOperationException($"KROT process '{role}' is already owned.");
            }
        }

        var runtime = LoadAndVerifyActiveRuntime();
        var marker = Guid.NewGuid();
        var startInfo = new ProcessStartInfo
        {
            FileName = runtime.ExecutablePath,
            Arguments = BuildCommandLine(arguments),
            WorkingDirectory = runtime.WorkingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        startInfo.EnvironmentVariables["KROT_OWNERSHIP_MARKER"] = marker.ToString("D");
        startInfo.EnvironmentVariables["KROT_PROCESS_ROLE"] = role;
        startInfo.EnvironmentVariables["PATH"] = runtime.DependencyDirectory
            + Path.PathSeparator
            + (startInfo.EnvironmentVariables["PATH"] ?? string.Empty);

        var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        var started = false;
        RuntimeProcessRecord? ownedRecord = null;
        try
        {
            if (!process.Start())
            {
                throw new InvalidOperationException("winws2 did not start.");
            }

            started = true;
            process.OutputDataReceived += (_, eventArgs) =>
            {
                if (!string.IsNullOrWhiteSpace(eventArgs.Data))
                {
                    _log.Info($"winws.{role}.stdout", eventArgs.Data);
                }
            };
            process.ErrorDataReceived += (_, eventArgs) =>
            {
                if (!string.IsNullOrWhiteSpace(eventArgs.Data))
                {
                    _log.Error($"winws.{role}.stderr", eventArgs.Data);
                }
            };
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            _job.Add(process);
            var record = new RuntimeProcessRecord
            {
                Role = role,
                ProcessId = process.Id,
                StartedUtc = process.StartTime.ToUniversalTime(),
                ExecutableSha256 = runtime.ExecutableSha256,
                OwnershipMarker = marker
            };

            lock (_sync)
            {
                _owned.Add(record);
            }

            ownedRecord = record;
            process.Exited += (_, _) => OnExited(record);
            await Task.Delay(750, cancellationToken).ConfigureAwait(false);
            if (process.HasExited)
            {
                throw new InvalidOperationException(
                    $"winws2 process '{role}' exited immediately with code {process.ExitCode}.");
            }

            _log.Info(
                "winws.started",
                $"Started KROT-owned {role} process. pid={record.ProcessId}, marker={marker:D}.");
        }
        catch
        {
            if (ownedRecord != null)
            {
                RemoveRecord(ownedRecord);
            }

            if (started && !process.HasExited)
            {
                process.Kill();
                process.WaitForExit(3000);
            }

            process.Dispose();
            throw;
        }
    }

    private async Task StopRoleAsync(string role, CancellationToken cancellationToken)
    {
        RuntimeProcessRecord[] records;
        lock (_sync)
        {
            records = _owned
                .Where(x => string.Equals(x.Role, role, StringComparison.OrdinalIgnoreCase))
                .ToArray();
        }

        foreach (var record in records)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await StopRecordAsync(record).ConfigureAwait(false);
        }
    }

    private Task StopRecordAsync(RuntimeProcessRecord record)
    {
        try
        {
            using var process = Process.GetProcessById(record.ProcessId);
            var actualStart = process.StartTime.ToUniversalTime();
            if (Math.Abs((actualStart - record.StartedUtc).TotalSeconds) > 1)
            {
                _log.Error(
                    "winws.ownership.mismatch",
                    $"Refused to stop pid={record.ProcessId}: process start time changed.");
                RemoveRecord(record);
                return Task.CompletedTask;
            }

            if (!process.HasExited)
            {
                process.Kill();
                process.WaitForExit(5000);
            }

            _log.Info(
                "winws.stopped",
                $"Stopped KROT-owned {record.Role} process. pid={record.ProcessId}.");
        }
        catch (ArgumentException)
        {
            // The owned process already exited.
        }
        finally
        {
            RemoveRecord(record);
        }

        return Task.CompletedTask;
    }

    private ActiveRuntime LoadAndVerifyActiveRuntime()
    {
        var manifestPath = SafePath("runtime-manifest.json");
        if (!File.Exists(manifestPath))
        {
            throw new FileNotFoundException("KROT runtime manifest is missing.", manifestPath);
        }

        var manifest = JsonConvert.DeserializeObject<RuntimeManifest>(File.ReadAllText(manifestPath))
            ?? throw new InvalidDataException("KROT runtime manifest is invalid.");
        if (string.IsNullOrWhiteSpace(manifest.ActiveZapret2))
        {
            throw new InvalidDataException("Active Zapret 2 runtime is not configured.");
        }

        var files = manifest.Files
            .Where(x => string.Equals(
                x.RuntimeId,
                manifest.ActiveZapret2,
                StringComparison.OrdinalIgnoreCase))
            .ToList();
        var sharedFiles = manifest.Files
            .Where(x => string.Equals(x.RuntimeId, "shared", StringComparison.OrdinalIgnoreCase))
            .ToList();
        foreach (var entry in files.Concat(sharedFiles))
        {
            var path = SafePath(entry.RelativePath);
            if (!_integrityVerifier.Verify(path, entry.Sha256))
            {
                throw new InvalidDataException(
                    $"Runtime integrity check failed for '{entry.RelativePath}'.");
            }
        }

        var executable = files.FirstOrDefault(x =>
            x.RelativePath.EndsWith("winws2.exe", StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidDataException("winws2.exe is not listed in the runtime manifest.");
        var executablePath = SafePath(executable.RelativePath);
        RequireFile(Path.Combine(Path.GetDirectoryName(executablePath)!, "WinDivert.dll"));
        RequireFile(Path.Combine(Path.GetDirectoryName(executablePath)!, "WinDivert64.sys"));
        RequireFile(Path.Combine(Path.GetDirectoryName(executablePath)!, "lua", "zapret-lib.lua"));
        RequireFile(Path.Combine(Path.GetDirectoryName(executablePath)!, "lua", "zapret-antidpi.lua"));
        var cygwinPath = sharedFiles
            .Where(x => x.RelativePath.EndsWith("cygwin1.dll", StringComparison.OrdinalIgnoreCase))
            .Select(x => SafePath(x.RelativePath))
            .FirstOrDefault()
            ?? throw new InvalidDataException("cygwin1.dll is not listed in the runtime manifest.");

        return new ActiveRuntime
        {
            ExecutablePath = executablePath,
            WorkingDirectory = Path.GetDirectoryName(executablePath)!,
            DependencyDirectory = Path.GetDirectoryName(cygwinPath)!,
            ExecutableSha256 = executable.Sha256
        };
    }

    private string SafePath(string relativePath)
    {
        var fullPath = Path.GetFullPath(Path.Combine(_runtimeRoot, relativePath));
        var rootPrefix = _runtimeRoot.TrimEnd(Path.DirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        if (!fullPath.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(fullPath, _runtimeRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("Runtime manifest contains an unsafe path.");
        }

        return fullPath;
    }

    private static void RequireFile(string path)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("Required Zapret runtime file is missing.", path);
        }
    }

    private static void ValidateArguments(IEnumerable<string> arguments)
    {
        foreach (var argument in arguments)
        {
            if (string.IsNullOrWhiteSpace(argument)
                || argument.IndexOfAny(new[] { '\r', '\n', '\0' }) >= 0)
            {
                throw new InvalidDataException("Unsafe winws2 argument.");
            }
        }
    }

    private static string BuildCommandLine(IEnumerable<string> arguments) =>
        string.Join(" ", arguments.Select(QuoteArgument));

    private static string QuoteArgument(string argument)
    {
        if (argument.Length > 0
            && argument.All(x => !char.IsWhiteSpace(x) && x != '"'))
        {
            return argument;
        }

        var result = new StringBuilder("\"");
        var backslashes = 0;
        foreach (var character in argument)
        {
            if (character == '\\')
            {
                backslashes++;
                continue;
            }

            if (character == '"')
            {
                result.Append('\\', backslashes * 2 + 1);
                result.Append('"');
                backslashes = 0;
                continue;
            }

            result.Append('\\', backslashes);
            backslashes = 0;
            result.Append(character);
        }

        result.Append('\\', backslashes * 2);
        result.Append('"');
        return result.ToString();
    }

    private void OnExited(RuntimeProcessRecord record)
    {
        RemoveRecord(record);
        _log.Info(
            "winws.exited",
            $"KROT-owned {record.Role} process exited. pid={record.ProcessId}.");
    }

    private void RemoveRecord(RuntimeProcessRecord record)
    {
        lock (_sync)
        {
            _owned.RemoveAll(x =>
                x.ProcessId == record.ProcessId
                && x.OwnershipMarker == record.OwnershipMarker);
        }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(RealZapretProcessManager));
        }
    }

    private static RuntimeProcessRecord Clone(RuntimeProcessRecord value) => new()
    {
        Role = value.Role,
        ProcessId = value.ProcessId,
        StartedUtc = value.StartedUtc,
        ExecutableSha256 = value.ExecutableSha256,
        OwnershipMarker = value.OwnershipMarker
    };

    private sealed class ActiveRuntime
    {
        public string ExecutablePath { get; set; } = string.Empty;

        public string WorkingDirectory { get; set; } = string.Empty;

        public string DependencyDirectory { get; set; } = string.Empty;

        public string ExecutableSha256 { get; set; } = string.Empty;
    }
}
