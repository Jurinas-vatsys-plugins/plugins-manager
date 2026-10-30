using System.Collections.ObjectModel;
using System.Diagnostics;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using PluginsManager.Models;

namespace PluginsManager.Services;

/// <summary>Shared state and the operations that several pages trigger (scan, catalog, install).</summary>
public sealed partial class AppServices : ObservableObject
{
    private const int MaxLogEntries = 500;

    public AppServices()
    {
        Store.Load();
        GitHub = new GitHubService();
        Plugins = new PluginService(Store, GitHub);
    }

    public SettingsStore Store { get; } = new();
    public AppSettings Settings => Store.Settings;
    public GitHubService GitHub { get; }
    public PluginService Plugins { get; }

    public ObservableCollection<LogEntry> Log { get; } = [];

    public IReadOnlyList<RemotePlugin> Catalog { get; private set; } = [];
    public IReadOnlyList<InstalledPlugin> Installed { get; private set; } = [];

    [ObservableProperty] private bool _isVatSysRunning;
    [ObservableProperty] private bool _isCatalogLoading;
    [ObservableProperty] private string? _catalogError;

    public event Action? InstalledChanged;
    public event Action? CatalogChanged;
    public event Action? SettingsChanged;

    public void Write(LogLevel level, string message)
    {
        void Add()
        {
            Log.Add(new LogEntry(DateTime.Now, level, message));
            while (Log.Count > MaxLogEntries) Log.RemoveAt(0);
        }

        if (Dispatcher.UIThread.CheckAccess()) Add();
        else Dispatcher.UIThread.Post(Add);
    }

    public void Info(string message) => Write(LogLevel.Info, message);
    public void Success(string message) => Write(LogLevel.Success, message);
    public void Warn(string message) => Write(LogLevel.Warning, message);
    public void Error(string message) => Write(LogLevel.Error, message);

    public void SaveSettings(AppSettings settings)
    {
        Store.Save(settings);
        Success("Settings saved.");
        SettingsChanged?.Invoke();
    }

    public void UpdateVatSysRunning() =>
        IsVatSysRunning = Process.GetProcessesByName("vatSys").Length > 0;

    public async Task RescanAsync()
    {
        var locations = Plugins.GetLocations();
        var autoUnblock = Settings.AutoUnblock;
        var (plugins, unblocked, errors) = await Task.Run(() =>
        {
            var found = new List<InstalledPlugin>();
            var failed = new List<string>();
            var count = 0;
            foreach (var location in locations)
            {
                try
                {
                    if (autoUnblock) count += Plugins.UnblockDlls(location);
                    found.AddRange(Plugins.Scan(location));
                }
                catch (Exception ex) { failed.Add($"Failed to scan {location.Path}: {ex.Message}"); }
            }
            return (found, count, failed);
        });

        errors.ForEach(Error);
        if (unblocked > 0) Success($"Unblocked {unblocked} DLL(s) (Unblock-File).");
        Installed = plugins;
        Info($"Found {plugins.Count} plugin(s) in {locations.Count} location(s).");
        InstalledChanged?.Invoke();
    }

    public async Task RefreshCatalogAsync()
    {
        if (IsCatalogLoading) return;
        IsCatalogLoading = true;
        CatalogError = null;
        Info($"Fetching plugins from {GitHubService.ApiHost} ...");
        try
        {
            var list = await GitHub.GetPluginsAsync();
            if (list.LastError is { Length: > 0 } apiError)
                Warn($"{GitHubService.ApiHost} could not reach GitHub on its last check, showing cached data: {apiError}");
            Catalog = list.Plugins
                .Where(p => !GitHubService.NonPluginRepos.Contains(p.Name))
                .OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
                .Select(p => p.ToRemote())
                .ToList();
            Success($"Found {Catalog.Count} plugin repositories.");
        }
        catch (Exception ex)
        {
            CatalogError = ex.Message;
            Error(ex.Message);
        }
        finally
        {
            IsCatalogLoading = false;
        }
        CatalogChanged?.Invoke();
    }

    /// <summary>Finds the GitHub plugin an installed item came from (install record first, then DLL file name).</summary>
    public RemotePlugin? MatchRemote(InstalledPlugin plugin)
    {
        if (plugin.Record is { } record)
            return Catalog.FirstOrDefault(c => string.Equals(c.Repo.Name, record.Repo, StringComparison.OrdinalIgnoreCase));
        if (plugin.IsDirectory) return null;
        return Catalog.FirstOrDefault(c => c.Asset is { IsDll: true } a
            && string.Equals(a.Name, plugin.ItemName, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Version installed locally: the tag recorded at install time, else the DLL file version.</summary>
    public static string? InstalledVersion(InstalledPlugin plugin) => plugin.Record?.Tag ?? plugin.Version;

    /// <summary>Asks for confirmation when vatSys is running, since loaded DLLs are locked.</summary>
    public async Task<bool> ConfirmIfVatSysRunningAsync()
    {
        UpdateVatSysRunning();
        if (!IsVatSysRunning) return true;
        return await DialogService.ConfirmAsync(
            "vatSys is running",
            "vatSys is currently running. Plugin files may be locked, and changes only take effect after vatSys is restarted.\n\nContinue anyway?",
            "Continue");
    }

    public async Task<bool> InstallAsync(RemotePlugin remote, PluginLocation location, IProgress<double>? progress = null)
    {
        if (!await ConfirmIfVatSysRunningAsync()) return false;

        var tag = remote.Release?.TagName ?? "?";
        Info($"Installing {remote.Repo.Name} {tag} to {location.Path} ...");
        try
        {
            var record = await Plugins.InstallAsync(remote, location, progress);
            Success($"Installed {remote.Repo.Name} {tag} ({string.Join(", ", record.Items)}) to {location.Name}."
                + (Settings.AutoUnblock ? " Files unblocked." : ""));
            await RescanAsync();
            return true;
        }
        catch (Exception ex)
        {
            Error($"Install of {remote.Repo.Name} failed: {ex.Message}");
            return false;
        }
    }
}
