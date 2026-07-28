using System;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using KROT.Core.Models;

namespace KROT.App.ViewModels;

public sealed class ServiceItemViewModel : ObservableObject
{
    private bool _isSelected;
    private bool _canEdit = true;

    public ServiceItemViewModel(
        ServiceId id,
        string iconGlyph,
        string iconSource,
        bool isSelected,
        Action<ServiceItemViewModel> selectionChanged,
        params ChannelIndicatorViewModel[] channels)
    {
        Id = id;
        IconGlyph = iconGlyph;
        IconSource = iconSource;
        _isSelected = isSelected;
        SelectionChanged = selectionChanged;
        Channels = new ObservableCollection<ChannelIndicatorViewModel>(channels);
    }

    public ServiceId Id { get; }

    public string IconGlyph { get; }

    public string IconSource { get; }

    public ObservableCollection<ChannelIndicatorViewModel> Channels { get; }

    public Action<ServiceItemViewModel> SelectionChanged { get; }

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
}
