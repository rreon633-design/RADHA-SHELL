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
            // A shell must never die because one panel threw; record it and keep the session alive.
            WriteCrashReport(e.Exception);
            e.Handled = true;
        };

        AppDomain.CurrentDomain.UnhandledException += (_, e) => WriteCrashReport(e.ExceptionObject as Exception);

        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            WriteCrashReport(e.Exception);
            e.SetObserved();
        };
    }

    /// <summary>
    /// Writes one report per failure into the log folder. Nothing is uploaded anywhere: the file is
    /// there for the user to attach, and it has already been through the same redactor the
    /// diagnostics bundle uses.
    /// </summary>
    private static void WriteCrashReport(Exception? exception)
    {
        try
        {
            var directory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Radha", "logs");

            var environment = new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["version"] = typeof(App).Assembly.GetName().Version?.ToString(),
                ["runtime"] = Environment.Version.ToString(),
                ["os"] = Environment.OSVersion.VersionString
            };

            Radha.Core.Diagnostics.CrashReport.Write(directory, exception, environment);
        }
        catch
        {
            // If even this fails there is nothing left to try; the process stays alive.
        }
    }

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        try
        {
            Program.Smoke?.Stage("ui");
            Ui.Initialize();
            Program.Smoke?.Stage("host");
            _host = new ShellHost();
            Program.Smoke?.Stage("start");
            await _host.StartAsync();
        }
        catch (Exception ex)
        {
            // Rethrowing here reaches nothing: an unpackaged WinExe has no console and the window
            // that would have shown the error is the thing that failed to build.
            Program.Fatal("the shell could not start", ex);
            if (Program.Smoke is { } failed) failed.Fail("boot", ex);
            Environment.Exit(1);
            return;
        }

        if (Program.Smoke is { } smoke)
        {
            // A window object exists, its XAML tree is built and it was created by the WinUI
            // compositor: the three things that fail when resources.pri or the Windows App SDK
            // runtime is missing. Content is checked because a Window with no tree is not a shell.
            var window = MainWindowInstance;
            if (window?.Content is null)
                smoke.Fail("window", new InvalidOperationException("The bar window was created without any content."));
            else
                smoke.Pass($"{window.GetType().Name} with content");
        }
    }
}
