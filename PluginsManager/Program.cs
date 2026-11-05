using Avalonia;

namespace PluginsManager;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        Services.SelfUpdater.CleanUpOldVersion();
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
}
