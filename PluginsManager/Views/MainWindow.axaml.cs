using System.Collections.Specialized;
using Avalonia.Controls;
using Avalonia.Threading;
using PluginsManager.Models;
using PluginsManager.ViewModels;

namespace PluginsManager.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        Opened += async (_, _) =>
        {
            if (DataContext is not MainWindowViewModel vm) return;
            vm.S.Log.CollectionChanged += OnLogChanged;
            await vm.InitializeAsync();
        };
    }

    private void OnLogChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action != NotifyCollectionChangedAction.Add) return;

        // The output panel is hidden by default; only errors open it.
        if (DataContext is MainWindowViewModel vm && e.NewItems?.OfType<LogEntry>().Any(l => l.Level == LogLevel.Error) == true)
            vm.IsLogVisible = true;
        Dispatcher.UIThread.Post(() => LogScroller.ScrollToEnd(), DispatcherPriority.Background);
    }
}
