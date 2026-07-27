using System;
using System.IO;
using System.IO.Pipes;
using System.Security.Principal;
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
    public event EventHandler<ServiceSnapshot>? SnapshotChanged;

    public Task<ServiceSnapshot> GetStatusAsync(CancellationToken cancellationToken) =>
        SendAsync("status", cancellationToken);

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var snapshot = await SendAsync("start", cancellationToken).ConfigureAwait(false);
        SnapshotChanged?.Invoke(this, snapshot);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        var snapshot = await SendAsync("stop", cancellationToken).ConfigureAwait(false);
        SnapshotChanged?.Invoke(this, snapshot);
    }

    private static async Task<ServiceSnapshot> SendAsync(string command, CancellationToken cancellationToken)
    {
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
        using var reader = new StreamReader(pipe, Encoding.UTF8, false, 4096, leaveOpen: true);
        var request = new ServiceRequest { Command = command };
        await writer.WriteLineAsync(JsonConvert.SerializeObject(request)).ConfigureAwait(false);
        var line = await reader.ReadLineAsync().ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        var response = JsonConvert.DeserializeObject<ServiceResponse>(line ?? string.Empty)
            ?? throw new InvalidOperationException("Empty response from KROT service.");
        if (!response.Success)
        {
            throw new InvalidOperationException(response.ErrorCode);
        }

        return response.Snapshot;
    }
}
