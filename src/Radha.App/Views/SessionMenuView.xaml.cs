using System.Diagnostics;
using Microsoft.UI.Xaml;
using Radha.Core.Security;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Radha.App.Views;

/// <summary>Session actions. These use the documented shutdown/exit APIs — no shell hooks required.</summary>
public sealed partial class SessionMenuView : UserControl
{
    /// <summary>Two-step confirmation: no modal dialog to click through reflexively.</summary>
    private readonly ConfirmGuard _confirm = new(TimeSpan.FromSeconds(4));

    public SessionMenuView() => InitializeComponent();

    private static void Run(string file, string arguments)
    {
        try
        {
            Process.Start(new ProcessStartInfo(file, arguments) { UseShellExecute = true, CreateNoWindow = true });
        }
        catch { /* policy may block; surfaced in logs later */ }
    }

    private void OnLogout(object sender, RoutedEventArgs e) => Run("shutdown.exe", "/l");

    private void OnSuspend(object sender, RoutedEventArgs e) =>
        Run("rundll32.exe", "powrprof.dll,SetSuspendState 0,1,0");

    /// <summary>Hibernation fails on a machine with the feature off; the call simply returns.</summary>
    private void OnHibernate(object sender, RoutedEventArgs e) => RunConfirmed("hibernate", "Hibernate", "shutdown.exe", "/h");

    private void OnReboot(object sender, RoutedEventArgs e) => RunConfirmed("reboot", "Restart", "shutdown.exe", "/r /t 0");

    private void OnShutdown(object sender, RoutedEventArgs e) => RunConfirmed("shutdown", "Shut down", "shutdown.exe", "/s /t 0");

    /// <summary>
    /// Sign-out and sleep are reversible in seconds, so they act immediately. Restart, shut down and
    /// hibernate lose everything unsaved, so they need a second, deliberate activation — and a
    /// mis-aimed second click on a different button replaces the arm instead of confirming it.
    /// </summary>
    private void RunConfirmed(string key, string label, string file, string arguments)
    {
        if (!_confirm.Confirm(key))
        {
            ConfirmText.Text = $"{label}: activate again to confirm, or ignore this and it expires.";
            ConfirmText.Visibility = Visibility.Visible;
            return;
        }

        ConfirmText.Visibility = Visibility.Collapsed;
        Run(file, arguments);
    }
}
