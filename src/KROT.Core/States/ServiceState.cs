namespace KROT.Core.States;

public enum ServiceState
{
    Disabled,
    NotChecked,
    Checking,
    Direct,
    UsingPreset,
    Working,
    PartiallyWorking,
    Failed
}

