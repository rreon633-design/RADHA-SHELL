using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;

namespace Radha.App.Converters;

/// <summary>bool → Visibility (true = Visible).</summary>
public sealed partial class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
        => value is true ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => value is Visibility.Visible;
}

/// <summary>bool → Visibility (true = Collapsed).</summary>
public sealed partial class InverseBoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
        => value is true ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => value is not Visibility.Visible;
}

/// <summary>0..100 → GridLength in stars, used for thin progress bars in the bar/OSD.</summary>
public sealed partial class PercentToGridLengthConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var percent = value is double d ? d : 0;
        percent = Math.Clamp(percent, 0, 100);
        return new GridLength(percent, GridUnitType.Star);
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => value is GridLength g ? g.Value : 0d;
}
