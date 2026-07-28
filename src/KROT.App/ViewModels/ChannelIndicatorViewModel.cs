using CommunityToolkit.Mvvm.ComponentModel;
using KROT.Core.States;

namespace KROT.App.ViewModels;

public sealed class ChannelIndicatorViewModel : ObservableObject
{
    private ChannelState _state = ChannelState.Unknown;
    private string _tooltip = string.Empty;
    private string _strategyId = string.Empty;

    public string Id { get; set; } = string.Empty;

    public string GeometryData { get; set; } = string.Empty;

    public string Tooltip
    {
        get => _tooltip;
        set => SetProperty(ref _tooltip, value);
    }

    public string TooltipKey { get; set; } = string.Empty;

    public string StrategyId
    {
        get => _strategyId;
        set => SetProperty(ref _strategyId, value);
    }

    public ChannelState State
    {
        get => _state;
        set => SetProperty(ref _state, value);
    }
}
