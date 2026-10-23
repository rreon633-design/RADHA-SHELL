using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Radha.App.ViewModels;

namespace Radha.App.Views;

public sealed partial class DashboardView : UserControl
{
    public DashboardView() => InitializeComponent();

    /// <summary>The wallpaper tab, configured by the host once services exist.</summary>
    public WallpaperPickerView Picker => WallpaperPicker;

    private void OnTileClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: TileViewModel tile })
            tile.Toggle();
    }
}
