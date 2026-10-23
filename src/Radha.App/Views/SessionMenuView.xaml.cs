using System.Diagnostics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Radha.App.Views;

/// <summary>Session actions. These use the documented shutdown/exit APIs — no shell hooks required.</summary>
public sealed partial class SessionMenuView : UserControl
{
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
    private void OnReboot(object sender, RoutedEventArgs e) => Run("shutdown.exe", "/r /t 0");
    private void OnShutdown(object sender, RoutedEventArgs e) => Run("shutdown.exe", "/s /t 0");
}
