using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Radha.App.ViewModels;

namespace Radha.App.Views;

/// <summary>
/// Notification list. Every row carries its source actions as buttons; the row's view model is
/// resolved by walking up the visual tree, because a template's DataContext is the action string.
/// </summary>
public sealed partial class NotificationCenterView : UserControl
{
    public NotificationCenterView() => InitializeComponent();

    private void OnActionClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement element || element.DataContext is not string action) return;
        if (FindNotification(element) is not { } item) return;

        Host?.RunNotificationAction(item, action);
    }

    private void OnDismissClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement element) return;
        if (FindNotification(element) is not { } item) return;

        Host?.DismissNotificationCommand.Execute(item.Id);
    }

    private static NotificationItemVm? FindNotification(DependencyObject start)
    {
        var current = start;
        while (current is not null)
        {
            if (current is FrameworkElement { DataContext: NotificationItemVm vm }) return vm;
            current = VisualTreeHelper.GetParent(current);
        }
        return null;
    }

    /// <summary>Set by the overlay that hosts this view; null in design-time previews.</summary>
    public ShellState? Host { get; set; }
}
