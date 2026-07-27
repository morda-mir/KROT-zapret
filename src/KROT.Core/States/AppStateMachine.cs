using System;
using System.Collections.Generic;

namespace KROT.Core.States;

public sealed class AppStateMachine
{
    private static readonly IReadOnlyDictionary<AppState, ISet<AppState>> AllowedTransitions =
        new Dictionary<AppState, ISet<AppState>>
        {
            [AppState.Off] = Set(AppState.Starting),
            [AppState.Starting] = Set(AppState.TestingDirect, AppState.Stopping, AppState.FatalError),
            [AppState.TestingDirect] = Set(AppState.TestingSavedProfiles, AppState.Running, AppState.Stopping, AppState.FatalError),
            [AppState.TestingSavedProfiles] = Set(AppState.SearchingProfiles, AppState.Running, AppState.PartialFailure, AppState.Stopping, AppState.FatalError),
            [AppState.SearchingProfiles] = Set(AppState.Running, AppState.PartialFailure, AppState.Stopping, AppState.FatalError),
            [AppState.Running] = Set(AppState.Stopping, AppState.PartialFailure, AppState.FatalError),
            [AppState.PartialFailure] = Set(AppState.Running, AppState.Stopping, AppState.FatalError),
            [AppState.FatalError] = Set(AppState.Stopping, AppState.Off),
            [AppState.Stopping] = Set(AppState.Off, AppState.FatalError)
        };

    public AppStateMachine(AppState initialState = AppState.Off)
    {
        State = initialState;
    }

    public AppState State { get; private set; }

    public event EventHandler<AppState>? StateChanged;

    public bool CanTransitionTo(AppState next) =>
        next == State || AllowedTransitions[State].Contains(next);

    public void TransitionTo(AppState next)
    {
        if (next == State)
        {
            return;
        }

        if (!CanTransitionTo(next))
        {
            throw new InvalidOperationException($"Invalid KROT state transition: {State} -> {next}.");
        }

        State = next;
        StateChanged?.Invoke(this, next);
    }

    private static ISet<AppState> Set(params AppState[] values) =>
        new HashSet<AppState>(values);
}

