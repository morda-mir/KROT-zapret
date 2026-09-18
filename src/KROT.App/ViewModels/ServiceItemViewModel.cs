using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KROT.Core.Models;

namespace KROT.App.ViewModels;

public sealed class ServiceItemViewModel : ObservableObject
{
    private bool _isSelected;
    private bool _canEdit = true;
    private string _refreshTooltip = string.Empty;

    public ServiceItemViewModel(
        ServiceId id,
        string iconGlyph,
        string iconSource,
        bool isSelected,
        Action<ServiceItemViewModel> selectionChanged,
        Func<ServiceItemViewModel, Task> refreshRequested,
        params ChannelIndicatorViewModel[] channels)
    {
        Id = id;
        IconGlyph = iconGlyph;
        IconSource = iconSource;
        _isSelected = isSelected;
        SelectionChanged = selectionChanged;
        RefreshCommand = new AsyncRelayCommand(
            () => refreshRequested(this),
            () => CanRefresh);
        Channels = new ObservableCollection<ChannelIndicatorViewModel>(channels);
    }

    public ServiceId Id { get; }

    public string IconGlyph { get; }

    public string IconSource { get; }

    public ObservableCollection<ChannelIndicatorViewModel> Channels { get; }

    public Action<ServiceItemViewModel> SelectionChanged { get; }

    public IAsyncRelayCommand RefreshCommand { get; }

    public bool CanRefresh { get; private set; }

    public string RefreshTooltip
    {
        get => _refreshTooltip;
        set => SetProperty(ref _refreshTooltip, value);
    }

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (SetProperty(ref _isSelected, value))
            {
                SelectionChanged(this);
            }
        }
    }

    public bool CanEdit
    {
        get => _canEdit;
        set => SetProperty(ref _canEdit, value);
    }

    public void RefreshChannels()
    {
        OnPropertyChanged(nameof(Channels));
    }

    public void UpdateCanRefresh(bool runtimeConnected)
    {
        var value = runtimeConnected
                    && IsSelected
                    && Channels.All(channel => !channel.IsBusy);
        if (CanRefresh == value)
        {
            return;
        }

        CanRefresh = value;
        OnPropertyChanged(nameof(CanRefresh));
        RefreshCommand.NotifyCanExecuteChanged();
    }
}
