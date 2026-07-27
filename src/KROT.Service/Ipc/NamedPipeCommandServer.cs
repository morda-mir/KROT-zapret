using System;
using System.IO;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using KROT.Core.Contracts;
using KROT.Core.Ipc;
using KROT.Core.Models;
using KROT.Service.Hosting;
using Newtonsoft.Json;

namespace KROT.Service.Ipc;

public sealed class NamedPipeCommandServer
{
    private readonly ServiceEngine _engine;
    private readonly ILogService _log;

    public NamedPipeCommandServer(ServiceEngine engine, ILogService log)
    {
        _engine = engine;
        _log = log;
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var userSid = InteractiveUserSidProvider.TryGetActiveUserSid();
            if (userSid == null)
            {
                await Task.Delay(1000, cancellationToken).ConfigureAwait(false);
                continue;
            }

            try
            {
                using var pipe = CreatePipe(userSid);
                using var registration = cancellationToken.Register(pipe.Dispose);
                await Task.Factory.FromAsync(
                    (callback, state) => pipe.BeginWaitForConnection(callback, state),
                    pipe.EndWaitForConnection,
                    null).ConfigureAwait(false);
                await HandleClientAsync(pipe, cancellationToken).ConfigureAwait(false);
            }
            catch (ObjectDisposedException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                _log.Error("pipe.failure", "Named Pipe request failed.", ex);
                await Task.Delay(250, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private async Task HandleClientAsync(NamedPipeServerStream pipe, CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(pipe, Encoding.UTF8, false, 4096, leaveOpen: true);
        using var writer = new StreamWriter(pipe, new UTF8Encoding(false), 4096, leaveOpen: true)
        {
            AutoFlush = true
        };

        var line = await reader.ReadLineAsync().ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        var request = JsonConvert.DeserializeObject<ServiceRequest>(line ?? string.Empty);
        var response = new ServiceResponse();

        try
        {
            if (request?.ProtocolVersion != ServiceProtocol.Version)
            {
                throw new InvalidOperationException("Unsupported IPC protocol version.");
            }

            switch (request.Command)
            {
                case "status":
                    break;
                case "start":
                    await _engine.StartAsync(
                        request.StartOptions ?? new KrotStartOptions(),
                        cancellationToken).ConfigureAwait(false);
                    break;
                case "stop":
                    await _engine.StopAsync(cancellationToken).ConfigureAwait(false);
                    break;
                default:
                    throw new InvalidOperationException("Unknown IPC command.");
            }

            response.Success = true;
            response.Snapshot = _engine.Snapshot;
        }
        catch (Exception ex)
        {
            response.Success = false;
            response.ErrorCode = "SERVICE_COMMAND_FAILED";
            response.Snapshot = _engine.Snapshot;
            _log.Error("pipe.command.failed", "Service command failed.", ex);
        }

        await writer.WriteLineAsync(JsonConvert.SerializeObject(response)).ConfigureAwait(false);
    }

    private static NamedPipeServerStream CreatePipe(SecurityIdentifier userSid)
    {
        var security = new PipeSecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        security.AddAccessRule(new PipeAccessRule(
            userSid,
            PipeAccessRights.ReadWrite,
            AccessControlType.Allow));
        security.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null),
            PipeAccessRights.FullControl,
            AccessControlType.Allow));
        security.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
            PipeAccessRights.FullControl,
            AccessControlType.Allow));

        return new NamedPipeServerStream(
            ServiceProtocol.PipeNameForSid(userSid.Value),
            PipeDirection.InOut,
            1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous,
            4096,
            4096,
            security);
    }
}
