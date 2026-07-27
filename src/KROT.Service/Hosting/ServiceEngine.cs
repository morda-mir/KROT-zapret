using System;
using System.Threading;
using System.Threading.Tasks;
using KROT.Core.Contracts;
using KROT.Core.Models;
using KROT.Core.States;
using KROT.Zapret.Profiles;

namespace KROT.Service.Hosting;

public sealed class ServiceEngine
{
    private readonly IZapretProcessManager _processManager;
    private readonly BuiltInPresetCatalog _presetCatalog;
    private readonly ILogService _log;
    private readonly bool _isFakeRuntime;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly AppStateMachine _stateMachine = new();

    public ServiceEngine(
        IZapretProcessManager processManager,
        BuiltInPresetCatalog presetCatalog,
        ILogService log,
        bool isFakeRuntime)
    {
        _processManager = processManager;
        _presetCatalog = presetCatalog;
        _log = log;
        _isFakeRuntime = isFakeRuntime;
    }

    public ServiceSnapshot Snapshot => new()
    {
        AppState = _stateMachine.State,
        MessageKey = MessageKeyFor(_stateMachine.State),
        IsFakeRuntime = _isFakeRuntime
    };

    public async Task StartAsync(
        KrotStartOptions options,
        CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_stateMachine.State != AppState.Off)
            {
                return;
            }

            _stateMachine.TransitionTo(AppState.Starting);
            _log.Info(
                "runtime.starting",
                $"Starting KROT runtime for {options.Services.Count} selected service(s).");

            _stateMachine.TransitionTo(AppState.TestingDirect);
            var plan = _presetCatalog.Build(options);

            _stateMachine.TransitionTo(AppState.TestingSavedProfiles);
            if (plan.HasMain)
            {
                await _processManager
                    .StartMainAsync(plan.MainArguments, cancellationToken)
                    .ConfigureAwait(false);
            }

            if (plan.HasVoice)
            {
                await _processManager
                    .StartVoiceAsync(plan.VoiceArguments, cancellationToken)
                    .ConfigureAwait(false);
            }

            _stateMachine.TransitionTo(AppState.Running);
            _log.Info(
                "runtime.running",
                $"KROT runtime active. presets={string.Join(",", plan.PresetIds)}.");
        }
        catch (OperationCanceledException)
        {
            await StopCoreAsync(CancellationToken.None).ConfigureAwait(false);
            throw;
        }
        catch (Exception ex)
        {
            _log.Error("runtime.start.failed", "Failed to start KROT runtime.", ex);
            try
            {
                await _processManager
                    .StopAllOwnedAsync(CancellationToken.None)
                    .ConfigureAwait(false);
            }
            catch (Exception cleanupException)
            {
                _log.Error(
                    "runtime.start.cleanup.failed",
                    "Failed to clean up KROT-owned processes after a start error.",
                    cleanupException);
            }

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
        _log.Info("runtime.stopped", "All KROT-owned processes stopped.");
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
