using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using ScrollKeeper.Extensions;
using ScrollKeeper.Presentation.Services;
using ScrollKeeper.Presentation.Views;
using ScrollKeeper.Services.Theming;

namespace ScrollKeeper;

/// <summary>
/// Interaction logic for App.axaml.
/// Composition root: builds the DI container and creates the main window from it.
/// </summary>
public partial class App : Application
{
    private ServiceProvider? _services;

    /// <summary>Root service provider. Prefer constructor injection; use this only where XAML creates objects for you.</summary>
    public static IServiceProvider Services =>
        (Current as App)?._services ?? throw new InvalidOperationException("Services are not initialized yet.");

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var services = _services = new ServiceCollection()
                .AddApplicationServices()
                .BuildServiceProvider(new ServiceProviderOptions
                {
                    ValidateOnBuild = true,
                    ValidateScopes = true
                });

            // Light or dark like the operating system (and Claude for desktop), before any window is created.
            services.GetRequiredService<IThemeService>().Initialize();

            var mainWindow = services.GetRequiredService<MainWindow>();
            desktop.MainWindow = mainWindow;
            desktop.ShutdownMode = ShutdownMode.OnMainWindowClose;
            desktop.Exit += (_, _) => services.Dispose();

            // Auto-update runs in the background once the window is on screen and reports through toasts:
            // download progress, then a countdown before ScrollKeeper restarts into the new version.
            mainWindow.Opened += OnMainWindowOpened;

            void OnMainWindowOpened(object? sender, EventArgs e)
            {
                mainWindow.Opened -= OnMainWindowOpened;
                _ = RunAutoUpdateAsync(services);
            }
        }

        base.OnFrameworkInitializationCompleted();
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

    /// <summary>Closes every window and ends the process (used before an update installer runs).</summary>
    public static void Shutdown()
    {
        if (Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.Shutdown();
    }
}
