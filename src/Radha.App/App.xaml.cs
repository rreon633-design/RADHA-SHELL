using Microsoft.UI.Xaml;
using Radha.App.Hosting;
using Radha.App.Services.Win;

namespace Radha.App;

/// <summary>
/// RADHA SHELL entry point (WinUI 3, unpackaged).
/// The generated Main() lives in the WinUI toolchain; OnLaunched boots the shell host.
/// </summary>
public partial class App : Application
{
    private ShellHost? _host;

    /// <summary>Set to the bar window at startup so interop helpers can query DPI.</summary>
    public static Window? MainWindowInstance { get; internal set; }

    public App()
    {
        InitializeComponent();
        UnhandledException += (_, e) =>
        {
            // A shell must never die because one panel threw; log and keep the session alive.
            System.Diagnostics.Debug.WriteLine($"[radha] unhandled: {e.Exception}");
            e.Handled = true;
        };
    }

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        Ui.Initialize();
        _host = new ShellHost();

        try
        {
            await _host.StartAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[radha] boot failed: {ex}");
            throw;
        }
    }
}
