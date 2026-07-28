using System;
using System.Threading;
using System.Threading.Tasks;
using KROT.Core.Contracts;
using KROT.Core.Models;
using KROT.Core.States;

namespace KROT.App.Services;

public sealed class FakeServiceClient : IServiceClient
{
    private readonly AppStateMachine _stateMachine = new();
    private KrotStartOptions? _options;

    public event EventHandler<ServiceSnapshot>? SnapshotChanged;

    public Task<ServiceSnapshot> GetStatusAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(CreateSnapshot());
    }

    public async Task StartAsync(KrotStartOptions options, CancellationToken cancellationToken)
    {
        if (_stateMachine.State != AppState.Off)
        {
            return;
        }

        _options = options;
        await SetStateAsync(AppState.Starting, 350, cancellationToken);
        await SetStateAsync(AppState.TestingDirect, 500, cancellationToken);
        await SetStateAsync(AppState.TestingSavedProfiles, 500, cancellationToken);
        await SetStateAsync(AppState.Running, 0, cancellationToken);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_stateMachine.State == AppState.Off)
        {
            return;
        }

        await SetStateAsync(AppState.Stopping, 350, cancellationToken);
        await SetStateAsync(AppState.Off, 0, cancellationToken);
        _options = null;
    }

    private async Task SetStateAsync(AppState state, int delayMilliseconds, CancellationToken cancellationToken)
    {
        _stateMachine.TransitionTo(state);
        SnapshotChanged?.Invoke(this, CreateSnapshot());
        if (delayMilliseconds > 0)
        {
            await Task.Delay(delayMilliseconds, cancellationToken);
        }
    }

    private ServiceSnapshot CreateSnapshot()
    {
        var snapshot = new ServiceSnapshot
        {
            AppState = _stateMachine.State,
            MessageKey = _stateMachine.State switch
            {
                AppState.Off => "Status.Off",
                AppState.Starting => "Status.Starting",
                AppState.TestingDirect => "Status.TestingDirect",
                AppState.TestingSavedProfiles or AppState.SearchingProfiles => "Status.Searching",
                AppState.Running => "Status.Running",
                AppState.Stopping => "Status.Stopping",
                _ => "Status.Error"
            },
            IsFakeRuntime = true
        };
        if (_options == null)
        {
            return snapshot;
        }

        Add(snapshot, ServiceId.Discord, "text");
        Add(snapshot, ServiceId.Discord, "media");
        Add(snapshot, ServiceId.Discord, "voice");
        Add(snapshot, ServiceId.YouTube, "site");
        Add(snapshot, ServiceId.YouTube, "video");
        return snapshot;
    }

    private void Add(
        ServiceSnapshot snapshot,
        ServiceId serviceId,
        string channelId)
    {
        var selected = _options?.Services.Contains(serviceId) == true;
        var state = !selected
            ? ChannelState.Disabled
            : _stateMachine.State switch
            {
                AppState.Starting or AppState.TestingDirect => ChannelState.Testing,
                AppState.TestingSavedProfiles or AppState.SearchingProfiles =>
                    ChannelState.Searching,
                AppState.Running => ChannelState.WorkingPreset,
                AppState.FatalError or AppState.PartialFailure => ChannelState.Failed,
                _ => ChannelState.Unknown
            };
        snapshot.Channels.Add(new ChannelSnapshot
        {
            ServiceId = serviceId,
            ChannelId = channelId,
            State = state
        });
    }
}
