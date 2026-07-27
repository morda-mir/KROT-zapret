using System;
using System.Threading;
using System.Threading.Tasks;
using KROT.Core.Contracts;
using KROT.Core.Models;
using KROT.Core.States;

namespace KROT.Service.Hosting;

public sealed class ServiceEngine
{
    private readonly IZapretProcessManager _processManager;
    private readonly ILogService _log;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly AppStateMachine _stateMachine = new();

    public ServiceEngine(IZapretProcessManager processManager, ILogService log)
    {
        _processManager = processManager;
        _log = log;
    }

    public ServiceSnapshot Snapshot => new()
    {
        AppState = _stateMachine.State,
        MessageKey = MessageKeyFor(_stateMachine.State),
        IsFakeRuntime = true
    };

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_stateMachine.State != AppState.Off)
            {
                return;
            }

            _stateMachine.TransitionTo(AppState.Starting);
            _log.Info("runtime.starting", "Starting fake KROT runtime.");
            await Task.Delay(100, cancellationToken).ConfigureAwait(false);

            _stateMachine.TransitionTo(AppState.TestingDirect);
            await Task.Delay(100, cancellationToken).ConfigureAwait(false);

            _stateMachine.TransitionTo(AppState.TestingSavedProfiles);
            await _processManager.StartMainAsync(Array.Empty<string>(), cancellationToken).ConfigureAwait(false);
            await _processManager.StartVoiceAsync(Array.Empty<string>(), cancellationToken).ConfigureAwait(false);

            _stateMachine.TransitionTo(AppState.Running);
            _log.Info("runtime.running", "Fake main and voice processes are active.");
        }
        catch (OperationCanceledException)
        {
            await StopCoreAsync(CancellationToken.None).ConfigureAwait(false);
            throw;
        }
        catch (Exception ex)
        {
            _log.Error("runtime.start.failed", "Failed to start KROT runtime.", ex);
            if (_stateMachine.CanTransitionTo(AppState.FatalError))
            {
                _stateMachine.TransitionTo(AppState.FatalError);
            }

            throw;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await StopCoreAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task StopCoreAsync(CancellationToken cancellationToken)
    {
        if (_stateMachine.State == AppState.Off)
        {
            return;
        }

        if (_stateMachine.CanTransitionTo(AppState.Stopping))
        {
            _stateMachine.TransitionTo(AppState.Stopping);
        }

        await _processManager.StopAllOwnedAsync(cancellationToken).ConfigureAwait(false);
        _stateMachine.TransitionTo(AppState.Off);
        _log.Info("runtime.stopped", "All KROT-owned fake processes stopped.");
    }

    private static string MessageKeyFor(AppState state)
    {
        return state switch
        {
            AppState.Off => "Status.Off",
            AppState.Starting => "Status.Starting",
            AppState.TestingDirect => "Status.TestingDirect",
            AppState.TestingSavedProfiles or AppState.SearchingProfiles => "Status.Searching",
            AppState.Running => "Status.Running",
            AppState.Stopping => "Status.Stopping",
            _ => "Status.Error"
        };
    }
}

