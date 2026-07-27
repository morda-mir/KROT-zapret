using CommunityToolkit.Mvvm.ComponentModel;
using KROT.Core.States;

namespace KROT.App.ViewModels;

public sealed class ChannelIndicatorViewModel : ObservableObject
{
    private ChannelState _state = ChannelState.Unknown;
    private string _tooltip = string.Empty;

    public string Symbol { get; set; } = "•";

    public string Tooltip
    {
        get => _tooltip;
        set => SetProperty(ref _tooltip, value);
    }

    public string TooltipKey { get; set; } = string.Empty;

    public ChannelState State
    {
        get => _state;
        set => SetProperty(ref _state, value);
    }
}
