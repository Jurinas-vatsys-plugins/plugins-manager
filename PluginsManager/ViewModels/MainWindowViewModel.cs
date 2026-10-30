using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PluginsManager.Services;

namespace PluginsManager.ViewModels;

public abstract class ViewModelBase : ObservableObject;

public sealed record NavItem(string Title, string Glyph, ViewModelBase Page);

public sealed partial class MainWindowViewModel : ViewModelBase
{
    private readonly DispatcherTimer _processTimer;

    public MainWindowViewModel()
    {
        Installed = new InstalledViewModel(S);
        Browse = new BrowseViewModel(S);
        Settings = new SettingsViewModel(S);
        NavItems =
        [
            new NavItem("Installed", "\uE8F1", Installed),
            new NavItem("Browse", "\uE896", Browse),
            new NavItem("Settings", "\uE713", Settings),
        ];
        _selectedNav = NavItems[0];

        _processTimer = new DispatcherTimer(TimeSpan.FromSeconds(3), DispatcherPriority.Background, (_, _) => S.UpdateVatSysRunning());
    }

    public AppServices S { get; } = new();

    public InstalledViewModel Installed { get; }
    public BrowseViewModel Browse { get; }
    public SettingsViewModel Settings { get; }
    public IReadOnlyList<NavItem> NavItems { get; }

    public string VersionText => $"v{AppInfo.Version}";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CurrentPage))]
    private NavItem _selectedNav;

    public ViewModelBase CurrentPage => SelectedNav.Page;

    [ObservableProperty] private bool _isLogVisible;
    [ObservableProperty] private bool _isManagerUpdateAvailable;
    [ObservableProperty] private string _managerUpdateText = "";
    [ObservableProperty] private string _managerStatusText = "";
    private string _managerReleaseUrl = GitHubService.ManagerUrl + "/releases";

    public async Task InitializeAsync()
    {
        S.UpdateVatSysRunning();
        _processTimer.Start();
        S.Info($"vatSys Plugin Manager v{AppInfo.Version} started. Data folder: {SettingsStore.DataDir}");

        await S.RescanAsync();
        var managerCheck = S.Settings.CheckForUpdatesOnStartup ? CheckManagerUpdateAsync() : Task.CompletedTask;
        await Task.WhenAll(managerCheck, S.RefreshCatalogAsync());
    }

    [RelayCommand]
    private async Task CheckManagerUpdateAsync()
    {
        ManagerStatusText = "Checking for updates...";
        try
        {
            var latest = await S.GitHub.GetLatestReleaseAsync(GitHubService.ManagerRepo);
            if (latest is null)
            {
                ManagerStatusText = "No release published";
                S.Info("Plugin Manager: no release has been published yet.");
                return;
            }

            _managerReleaseUrl = latest.HtmlUrl;
            if (VersionUtil.IsNewer(latest.TagName, AppInfo.Version))
            {
                ManagerUpdateText = $"A new version of vatSys Plugin Manager is available: {latest.TagName} (you have v{AppInfo.Version}).";
                IsManagerUpdateAvailable = true;
                ManagerStatusText = $"Update: {latest.TagName}";
                S.Warn(ManagerUpdateText);
            }
            else
            {
                ManagerStatusText = "Up to date";
                S.Info($"Plugin Manager is up to date (latest release {latest.TagName}).");
            }
        }
        catch (Exception ex)
        {
            ManagerStatusText = "Update check failed";
            S.Error($"Update check failed: {ex.Message}");
        }
    }

    [RelayCommand]
    private void OpenManagerRelease() => DialogService.Open(_managerReleaseUrl);

    [RelayCommand]
    private void DismissManagerUpdate() => IsManagerUpdateAvailable = false;

    [RelayCommand]
    private void ToggleLog() => IsLogVisible = !IsLogVisible;

    [RelayCommand]
    private void ClearLog() => S.Log.Clear();
}
