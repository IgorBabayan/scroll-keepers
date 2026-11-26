using Avalonia;

namespace ScrollKeeper;

internal static class Program
{
    /// <summary>
    /// Entry point. Nothing that touches Avalonia may run before <see cref="AppBuilder"/> is configured.
    /// </summary>
    [STAThread]
    public static void Main(string[] args) => BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);

    /// <summary>Also used by the Avalonia previewer of IDEs.</summary>
    public static AppBuilder BuildAvaloniaApp() => AppBuilder
        .Configure<App>()
        .UsePlatformDetect()
        .WithInterFont()
        .LogToTrace();
}
