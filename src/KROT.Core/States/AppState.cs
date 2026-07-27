namespace KROT.Core.States;

public enum AppState
{
    Off,
    Starting,
    TestingDirect,
    TestingSavedProfiles,
    SearchingProfiles,
    Running,
    Stopping,
    PartialFailure,
    FatalError
}

