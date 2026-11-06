using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using PluginsManager.Models;
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
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(UpdateManagerCommand))]
    private bool _isSelfUpdating;

    private string _managerReleaseUrl = GitHubService.ManagerUrl + "/releases";
    private GhRelease? _managerRelease;

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
            _managerRelease = latest;
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

    private bool CanUpdateManager() => !IsSelfUpdating;

    [RelayCommand(CanExecute = nameof(CanUpdateManager))]
    private async Task UpdateManagerAsync()
    {
        if (_managerRelease is not { } release) return;
        var asset = SelfUpdater.FindAsset(release, SelfUpdater.Current);
        if (asset is null || !SelfUpdater.IsSupported || SelfUpdater.ExePath is not { } exe)
        {
            S.Warn("This copy cannot update itself; download the new version from the release page.");
            OpenManagerRelease();
            return;
        }

        var confirmed = await DialogService.ConfirmAsync(
            "Update vatSys Plugin Manager",
            $"Update from v{AppInfo.Version} to {release.TagName}?\n\nThe new version is downloaded from GitHub and the app restarts.",
            "Update and restart");
        if (!confirmed) return;

        IsSelfUpdating = true;
        var banner = ManagerUpdateText;
        try
        {
            S.Info($"Downloading vatSys Plugin Manager {release.TagName} ...");
            await SelfUpdater.InstallAsync(S.GitHub, asset, SelfUpdater.Current, exe,
                new Progress<double>(p => ManagerUpdateText = $"Downloading {release.TagName} ... {p:P0}"));
            S.Success($"Updated to {release.TagName}. Restarting ...");
            SelfUpdater.Restart(exe);
            (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.Shutdown();
        }
        catch (Exception ex)
        {
            ManagerUpdateText = banner;
            S.Error($"Self-update failed: {ex.Message}");
            if (await DialogService.ConfirmAsync("Update failed",
                    $"{ex.Message}\n\nOpen the release page to download the new version manually?", "Open release page"))
                OpenManagerRelease();
        }
        finally
        {
            IsSelfUpdating = false;
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
