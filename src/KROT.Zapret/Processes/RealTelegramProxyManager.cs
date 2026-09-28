using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using KROT.Core.Contracts;
using KROT.Zapret.Runtime;
using Newtonsoft.Json;

namespace KROT.Zapret.Processes;

public sealed class RealTelegramProxyManager : ITelegramProxyManager, IDisposable
{
    private static readonly TimeSpan StartupTimeout = TimeSpan.FromSeconds(20);
    private readonly object _sync = new();
    private readonly string _runtimeRoot;
    private readonly ILogService _log;
    private readonly FileIntegrityVerifier _integrityVerifier = new();
    private readonly HashSet<int> _ownedDescendants = new();
    private Process? _process;
    private WindowsJobObject? _job;
    private string? _activeExecutablePath;
    private bool _disposed;

    public RealTelegramProxyManager(string runtimeRoot, ILogService log)
    {
        _runtimeRoot = Path.GetFullPath(runtimeRoot);
        _log = log;
    }

    public bool IsRunning
    {
        get
        {
            lock (_sync)
            {
                return _process != null && !_process.HasExited;
            }
        }
    }

    public async Task StartAsync(string secret, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfDisposed();
        ValidateSecret(secret);
        lock (_sync)
        {
            if (_process != null && !_process.HasExited)
            {
                throw new InvalidOperationException("KROT Telegram proxy is already running.");
            }
        }

        var runtime = LoadAndVerifyRuntime();
        var ready = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var exited = new TaskCompletionSource<int>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var processId = 0;
        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = runtime.ExecutablePath,
                Arguments = BuildCommandLine(new[]
                {
                    "--host", "127.0.0.1",
                    "--port", "1443",
                    "--secret", secret
                }),
                WorkingDirectory = runtime.WorkingDirectory,
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            },
            EnableRaisingEvents = true
        };
        var job = new WindowsJobObject();

        void HandleLine(string? line, bool isError)
        {
            if (line == null || string.IsNullOrWhiteSpace(line))
            {
                return;
            }

            if (line.IndexOf("Listening on", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                ready.TrySetResult(true);
            }

            if (ContainsSecret(line))
            {
                return;
            }

            if (isError)
            {
                _log.Detail("telegram.proxy.stderr", line);
            }
            else
            {
                _log.Detail("telegram.proxy.stdout", line);
            }
        }

        process.OutputDataReceived += (_, args) => HandleLine(args.Data, isError: false);
        process.ErrorDataReceived += (_, args) => HandleLine(args.Data, isError: true);
        process.Exited += (_, _) =>
        {
            int exitCode;
            try
            {
                exitCode = process.ExitCode;
            }
            catch (InvalidOperationException)
            {
                exitCode = -1;
            }

            WindowsJobObject? exitedJob = null;
            string? exitedExecutablePath = null;
            int[] exitedDescendants = Array.Empty<int>();
            lock (_sync)
            {
                if (ReferenceEquals(_process, process))
                {
                    _process = null;
                    exitedJob = _job;
                    _job = null;
                    exitedExecutablePath = _activeExecutablePath;
                    _activeExecutablePath = null;
                    exitedDescendants = _ownedDescendants.ToArray();
                    _ownedDescendants.Clear();
                }
            }

            StopOwnedDescendants(exitedDescendants, exitedExecutablePath);
            exitedJob?.Dispose();
            exited.TrySetResult(exitCode);
            _log.Info(
                "telegram.proxy.exited",
                $"KROT-owned Telegram proxy exited. pid={processId}, code={exitCode}.");
        };

        try
        {
            if (!process.Start())
            {
                throw new InvalidOperationException("Telegram proxy did not start.");
            }
            processId = process.Id;

            lock (_sync)
            {
                _process = process;
                _job = job;
                _activeExecutablePath = runtime.ExecutablePath;
                _ownedDescendants.Clear();
            }

            job.Add(process);
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            var timeout = Task.Delay(StartupTimeout, cancellationToken);
            var completed = await Task.WhenAny(ready.Task, exited.Task, timeout)
                .ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            if (completed == exited.Task)
            {
                throw new InvalidOperationException(
                    $"Telegram proxy exited during startup with code {await exited.Task.ConfigureAwait(false)}.");
            }

            if (completed != ready.Task)
            {
                throw new TimeoutException("Telegram proxy did not become ready in time.");
            }

            var descendants = ProcessTree.DescendantsOf(process.Id);
            lock (_sync)
            {
                if (ReferenceEquals(_process, process))
                {
                    _ownedDescendants.UnionWith(descendants);
                }
            }

            _log.Info(
                "telegram.proxy.started",
                $"Started KROT-owned Telegram proxy. pid={process.Id}, sha256={runtime.ExecutableSha256}.");
        }
        catch
        {
            await StopProcessAsync(
                    process,
                    job,
                    Array.Empty<int>(),
                    runtime.ExecutablePath)
                .ConfigureAwait(false);
            throw;
        }
    }

    public async Task RestartAsync(string secret, CancellationToken cancellationToken)
    {
        await StopAsync(cancellationToken).ConfigureAwait(false);
        await StartAsync(secret, cancellationToken).ConfigureAwait(false);
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Process? process;
        WindowsJobObject? job;
        int[] descendants;
        string? executablePath;
        lock (_sync)
        {
            process = _process;
            _process = null;
            job = _job;
            _job = null;
            descendants = _ownedDescendants.ToArray();
            _ownedDescendants.Clear();
            executablePath = _activeExecutablePath;
            _activeExecutablePath = null;
        }

        if (process == null)
        {
            job?.Dispose();
            return Task.CompletedTask;
        }

        return StopProcessAsync(process, job, descendants, executablePath);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        StopAsync(CancellationToken.None).GetAwaiter().GetResult();
    }

    private ActiveTelegramRuntime LoadAndVerifyRuntime()
    {
        var manifestPath = SafePath("runtime-manifest.json");
        if (!File.Exists(manifestPath))
        {
            throw new FileNotFoundException("KROT runtime manifest is missing.", manifestPath);
        }

        var manifest = JsonConvert.DeserializeObject<RuntimeManifest>(File.ReadAllText(manifestPath))
            ?? throw new InvalidDataException("KROT runtime manifest is invalid.");
        if (string.IsNullOrWhiteSpace(manifest.ActiveTelegramProxy))
        {
            throw new InvalidDataException("Telegram proxy runtime is not configured.");
        }

        var files = manifest.Files
            .Where(entry => string.Equals(
                entry.RuntimeId,
                manifest.ActiveTelegramProxy,
                StringComparison.OrdinalIgnoreCase))
            .ToList();
        foreach (var entry in files)
        {
            var path = SafePath(entry.RelativePath);
            if (!_integrityVerifier.Verify(path, entry.Sha256))
            {
                throw new InvalidDataException(
                    $"Runtime integrity check failed for '{entry.RelativePath}'.");
            }
        }

        var executable = files.SingleOrDefault(entry =>
            entry.RelativePath.EndsWith(
                "TgWsProxy_console.exe",
                StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidDataException(
                "TgWsProxy_console.exe is not listed in the runtime manifest.");
        var executablePath = SafePath(executable.RelativePath);
        return new ActiveTelegramRuntime
        {
            ExecutablePath = executablePath,
            WorkingDirectory = Path.GetDirectoryName(executablePath)!,
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

    private async Task StopProcessAsync(
        Process process,
        WindowsJobObject? job,
        IReadOnlyCollection<int> knownDescendants,
        string? executablePath)
    {
        try
        {
            var descendants = new HashSet<int>(knownDescendants);
            try
            {
                descendants.UnionWith(ProcessTree.DescendantsOf(process.Id));
            }
            catch (InvalidOperationException)
            {
                // The root process already exited.
            }

            StopOwnedDescendants(descendants, executablePath);
            job?.Dispose();
            if (!process.HasExited)
            {
                process.Kill();
                await Task.Run(() => process.WaitForExit(5000)).ConfigureAwait(false);
            }

            _log.Info(
                "telegram.proxy.stopped",
                $"Stopped KROT-owned Telegram proxy. pid={process.Id}.");
        }
        catch (InvalidOperationException)
        {
            // The process already exited.
        }
        finally
        {
            lock (_sync)
            {
                if (ReferenceEquals(_process, process))
                {
                    _process = null;
                }

                if (ReferenceEquals(_job, job))
                {
                    _job = null;
                }

                _ownedDescendants.Clear();
                _activeExecutablePath = null;
            }

            job?.Dispose();
            process.Dispose();
        }
    }

    private static void StopOwnedDescendants(
        IEnumerable<int> processIds,
        string? executablePath)
    {
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            return;
        }

        foreach (var processId in processIds.Distinct().Reverse())
        {
            try
            {
                using var process = Process.GetProcessById(processId);
                var candidatePath = process.MainModule?.FileName;
                if (!string.Equals(
                        candidatePath,
                        executablePath,
                        StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (!process.HasExited)
                {
                    process.Kill();
                    process.WaitForExit(5000);
                }
            }
            catch (ArgumentException)
            {
                // The descendant already exited.
            }
            catch (InvalidOperationException)
            {
                // The descendant already exited.
            }
            catch (System.ComponentModel.Win32Exception)
            {
                // The descendant exited while its executable path was inspected.
            }
        }
    }

    private static bool ContainsSecret(string line) =>
        line.IndexOf("Secret:", StringComparison.OrdinalIgnoreCase) >= 0
        || line.IndexOf("tg://proxy?", StringComparison.OrdinalIgnoreCase) >= 0;

    private static void ValidateSecret(string secret)
    {
        if (secret == null
            || secret.Length != 32
            || secret.Any(character => !Uri.IsHexDigit(character)))
        {
            throw new InvalidDataException(
                "Telegram proxy secret must contain exactly 32 hexadecimal characters.");
        }
    }

    private static string BuildCommandLine(string[] arguments) =>
        string.Join(" ", arguments.Select(QuoteArgument));

    private static string QuoteArgument(string argument)
    {
        if (argument.Length > 0
            && argument.All(character => !char.IsWhiteSpace(character) && character != '"'))
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

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(RealTelegramProxyManager));
        }
    }

    private sealed class ActiveTelegramRuntime
    {
        public string ExecutablePath { get; set; } = string.Empty;

        public string WorkingDirectory { get; set; } = string.Empty;

        public string ExecutableSha256 { get; set; } = string.Empty;
    }
}
