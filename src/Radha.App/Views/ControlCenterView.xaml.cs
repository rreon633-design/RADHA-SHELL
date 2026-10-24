using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Radha.App.ViewModels;

namespace Radha.App.Views;

public sealed partial class ControlCenterView : UserControl
{
    public ControlCenterView() => InitializeComponent();

    private ControlCenterViewModel? Model => DataContext as ControlCenterViewModel;

    private void OnTileClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: TileViewModel tile }) return;

        // A tile whose hardware or API is missing stays put and says why, rather than animating a
        // state change that did not happen.
        if (!tile.IsAvailable)
        {
            tile.Detail = "Unavailable here";
            return;
        }

        tile.Toggle();
    }

    private void OnMuteVolume(object sender, RoutedEventArgs e)
    {
        if (Model is { } model) model.IsMuted = !model.IsMuted;
    }

    private void OnMuteMic(object sender, RoutedEventArgs e)
    {
        if (Model is { } model) model.Microphone = model.Microphone > 0 ? 0 : 100;
    }

    /// <summary>
    /// The brightness glyph doubles as the explanation on machines whose display cannot obey:
    /// external panels without DDC/CI, and virtual displays in a VM.
    /// </summary>
    private void OnBrightnessUnsupported(object sender, RoutedEventArgs e)
    {
        if (Model is { BrightnessAvailable: false } model)
            model.BrightnessText = "No DDC";
    }
}
