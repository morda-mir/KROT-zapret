using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using KROT.Core.States;

namespace KROT.App.Converters;

public sealed class StateBrushConverter : IValueConverter
{
    private static readonly Brush Gray = new SolidColorBrush(Color.FromRgb(92, 104, 114));
    private static readonly Brush Yellow = new SolidColorBrush(Color.FromRgb(242, 191, 77));
    private static readonly Brush Green = new SolidColorBrush(Color.FromRgb(80, 216, 144));
    private static readonly Brush Red = new SolidColorBrush(Color.FromRgb(237, 98, 98));

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value switch
        {
            ChannelState.WaitingForActivity
                or ChannelState.Testing
                or ChannelState.Searching => Yellow,
            ChannelState.WorkingDirect or ChannelState.WorkingPreset => Green,
            ChannelState.Failed => Red,
            AppState.Starting or AppState.TestingDirect or AppState.TestingSavedProfiles or AppState.SearchingProfiles or AppState.Stopping => Yellow,
            AppState.Running => Green,
            AppState.PartialFailure or AppState.FatalError => Red,
            _ => Gray
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
