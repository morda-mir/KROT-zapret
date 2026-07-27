using System;
using KROT.Core.States;
using Xunit;

namespace KROT.Core.Tests;

public sealed class AppStateMachineTests
{
    [Fact]
    public void HappyPath_ReachesRunningAndReturnsToOff()
    {
        var machine = new AppStateMachine();

        machine.TransitionTo(AppState.Starting);
        machine.TransitionTo(AppState.TestingDirect);
        machine.TransitionTo(AppState.TestingSavedProfiles);
        machine.TransitionTo(AppState.Running);
        machine.TransitionTo(AppState.Stopping);
        machine.TransitionTo(AppState.Off);

        Assert.Equal(AppState.Off, machine.State);
    }

    [Fact]
    public void InvalidTransition_IsRejected()
    {
        var machine = new AppStateMachine();

        Assert.Throws<InvalidOperationException>(() => machine.TransitionTo(AppState.Running));
    }
}

