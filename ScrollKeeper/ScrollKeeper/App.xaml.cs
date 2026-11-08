using System.Windows;
using ScrollKeeper.Extensions;
using ScrollKeeper.Presentation.Services;
using ScrollKeeper.Presentation.Views;
using ScrollKeeper.Services.Theming;

namespace ScrollKeeper;

/// <summary>
/// Interaction logic for App.xaml.
/// Composition root: builds the DI container and creates the main window from it.
/// </summary>
public partial class App : Application
{
    private ServiceProvider? _services;

    /// <summary>Root service provider. Prefer constructor injection; use this only where WPF creates objects for you.</summary>
    public static IServiceProvider Services =>
        ((App)Current)._services ?? throw new InvalidOperationException("Services are not initialized yet.");

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _services = new ServiceCollection()
            .AddApplicationServices()
            .BuildServiceProvider(new ServiceProviderOptions
            {
                ValidateOnBuild = true,
                ValidateScopes = true
            });

        // Light or dark like Windows (and Claude for desktop), before any window is created.
        _services.GetRequiredService<IThemeService>().Initialize();

        var mainWindow = _services.GetRequiredService<MainWindow>();
        MainWindow = mainWindow;
        mainWindow.Show();

        // Auto-update runs in the background and reports through toasts:
        // download progress, then a countdown before ScrollKeeper restarts into the new version.
        _ = RunAutoUpdateAsync(_services);
    }

    private static async Task RunAutoUpdateAsync(IServiceProvider services)
    {
        try
        {
            await services.GetRequiredService<IAutoUpdater>().RunOnStartupAsync();
        }
        catch (Exception ex)
        {
            // An update problem must never take the app down.
            System.Diagnostics.Debug.WriteLine($"Auto-update failed: {ex}");
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _services?.Dispose();
        base.OnExit(e);
    }
}
