using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using PluginsManager.Services;
using Avalonia.Markup.Xaml;
using PluginsManager.ViewModels;
using PluginsManager.Views;

namespace PluginsManager;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var window = new MainWindow { DataContext = new MainWindowViewModel() };
            desktop.MainWindow = window;
            SingleInstance.ListenForActivation(() => Dispatcher.UIThread.Post(() =>
            {
                if (window.WindowState == WindowState.Minimized) window.WindowState = WindowState.Normal;
                window.Activate();
                window.Topmost = true; // Windows only lets a background app come forward this way
                window.Topmost = false;
            }));
        }

        base.OnFrameworkInitializationCompleted();
    }
}
