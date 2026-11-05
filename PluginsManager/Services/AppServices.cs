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
        if (await AnalyzeAsync()) InstalledChanged?.Invoke();
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
            await AnalyzeAsync();
            CatalogChanged?.Invoke();

            // First run (or new releases): inspect release assets, then match hand-installed plugins again.
            await IndexCatalogAsync();
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
        await AnalyzeAsync();
        CatalogChanged?.Invoke();
    }

    private Dictionary<InstalledPlugin, PluginStatus> _status = [];
    private readonly Dictionary<string, (DateTime Time, long Length, string Hash)> _hashCache = new(StringComparer.OrdinalIgnoreCase);

    public PluginStatus Status(InstalledPlugin plugin) =>
        _status.TryGetValue(plugin, out var s) ? s : new PluginStatus { DisplayVersion = plugin.Record?.Tag ?? plugin.Version };

    /// <summary>Downloads release assets that are not in the index yet, so hand-installed plugins can be recognised.</summary>
    private async Task IndexCatalogAsync()
    {
        var missing = Catalog
            .Where(c => c.Asset is { } a && !Store.AssetIndex.Any(e => e.Url == a.BrowserDownloadUrl))
            .ToList();
        if (missing.Count == 0) return;

        var entries = await Task.WhenAll(missing.Select(async remote =>
        {
            try { return await Plugins.IndexAssetAsync(remote); }
            catch (Exception ex)
            {
                Warn($"Could not inspect the {remote.Repo.Name} release ({ex.Message}); hand-installed copies may not be recognised.");
                return null;
            }
        }));

        // Keep only entries for assets still in the catalog, so the cache does not grow forever.
        var current = Catalog.Select(c => c.Asset?.BrowserDownloadUrl).OfType<string>().ToHashSet();
        Store.AssetIndex.RemoveAll(e => !current.Contains(e.Url));
        Store.AssetIndex.AddRange(entries.OfType<AssetIndexEntry>());
        try { Store.SaveAssetIndex(); } catch { /* cache only */ }
    }

    private static AssetIndexEntry? IndexFor(RemotePlugin remote, IReadOnlyList<AssetIndexEntry> index) =>
        remote.Asset is { } a ? index.FirstOrDefault(e => e.Url == a.BrowserDownloadUrl) : null;

    /// <summary>Matches every installed plugin to the catalog, works out its update state and finds duplicates.</summary>
    private int _analysisGeneration;
    private int _lastDuplicateCount;

    /// <returns>False when a newer analysis started meanwhile and this result was discarded.</returns>
    private async Task<bool> AnalyzeAsync()
    {
        var generation = ++_analysisGeneration;
        var installed = Installed;
        // Snapshots taken on the UI thread: the index and catalog are only modified there.
        var catalog = Catalog;
        var index = Store.AssetIndex.ToList();
        var status = await Task.Run(() =>
        {
            var result = installed.ToDictionary(p => p, p => Evaluate(p, catalog, index));

            var enabled = installed.Where(p => p.IsEnabled).ToList();
            foreach (var global in enabled.Where(p => p.Location.IsGlobal))
            foreach (var profile in enabled.Where(p => !p.Location.IsGlobal))
            {
                if (!SamePlugin(global, result[global], profile, result[profile])) continue;
                result[global].Duplicates.Add(profile);
                result[profile].Duplicates.Add(global);
            }
            return result;
        });
        if (generation != _analysisGeneration) return false;
        _status = status;

        var duplicated = status.Count(s => s.Key.Location.IsGlobal && s.Value.Duplicates.Count > 0);
        if (duplicated != _lastDuplicateCount && duplicated > 0)
            Warn($"{duplicated} plugin(s) in the vatSys directory are also installed in a profile; vatSys loads both copies.");
        _lastDuplicateCount = duplicated;
        return true;
    }

    /// <summary>Finds which catalog plugin an installed item is: install record, then release DLL names, then folder name.</summary>
    private static RemotePlugin? MatchRemote(
        InstalledPlugin plugin, IReadOnlyList<RemotePlugin> catalog, IReadOnlyList<AssetIndexEntry> index)
    {
        if (plugin.Record is { } record)
        {
            var byRecord = catalog.FirstOrDefault(c => string.Equals(c.Repo.Name, record.Repo, StringComparison.OrdinalIgnoreCase));
            if (byRecord is not null) return byRecord;
        }

        RemotePlugin? best = null;
        var bestScore = 0;
        foreach (var remote in catalog)
        {
            if (IndexFor(remote, index) is not { } entry) continue;
            var score = plugin.PluginDlls.Keys.Count(k => entry.Dlls.ContainsKey(k.ToLowerInvariant())) * 2
                + (entry.Items.Contains(plugin.ItemName, StringComparer.OrdinalIgnoreCase) ? 1 : 0);
            if (score > bestScore) (best, bestScore) = (remote, score);
        }
        if (best is not null) return best;

        var key = Normalize(plugin.Name);
        return catalog.FirstOrDefault(c => Normalize(c.Repo.Name) == key);
    }

    private PluginStatus Evaluate(
        InstalledPlugin plugin, IReadOnlyList<RemotePlugin> catalog, IReadOnlyList<AssetIndexEntry> index)
    {
        var remote = MatchRemote(plugin, catalog, index);
        var status = new PluginStatus { Remote = remote, DisplayVersion = plugin.Record?.Tag ?? plugin.Version };
        if (remote?.Release is not { } release || remote.Asset is null) return status;
        var tag = release.TagName;

        // Compare file contents with the latest release: DLL file versions are often not bumped between releases.
        if (IndexFor(remote, index) is { } entry)
        {
            var shared = plugin.PluginDlls.Where(d => entry.Dlls.ContainsKey(d.Key.ToLowerInvariant())).ToList();
            if (shared.Count > 0)
            {
                var identical = shared.All(d => TryHash(d.Value) is { } h
                    && string.Equals(h, entry.Dlls[d.Key.ToLowerInvariant()], StringComparison.OrdinalIgnoreCase));
                if (identical)
                {
                    status.IsLatest = true;
                    status.DisplayVersion = tag;
                    return status;
                }

                // Different files: an update, unless the local DLL reports a newer version (e.g. a dev build).
                status.DisplayVersion = plugin.Version;
                var localIsNewer = VersionUtil.Parse(plugin.Version) is { } local
                    && VersionUtil.Parse(tag) is { } latest && local > latest;
                status.HasUpdate = !localIsNewer;
                return status;
            }
        }

        status.HasUpdate = VersionUtil.IsNewer(tag, status.DisplayVersion);
        status.IsLatest = !status.HasUpdate && status.DisplayVersion is not null && VersionUtil.Parse(tag) == VersionUtil.Parse(status.DisplayVersion);
        return status;
    }

    private static bool SamePlugin(InstalledPlugin a, PluginStatus sa, InstalledPlugin b, PluginStatus sb) =>
        (sa.Remote is not null && sa.Remote == sb.Remote)
        || a.PluginDlls.Keys.Any(k => b.PluginDlls.ContainsKey(k));

    /// <summary>Enabled copies of <paramref name="plugin"/> in the other scope (vatSys directory vs. profiles).</summary>
    public List<InstalledPlugin> ConflictsFor(InstalledPlugin plugin)
    {
        var s = Status(plugin);
        return Installed
            .Where(o => o.IsEnabled && o.Location.IsGlobal != plugin.Location.IsGlobal && SamePlugin(plugin, s, o, Status(o)))
            .ToList();
    }

    private string? TryHash(string path)
    {
        try
        {
            var info = new FileInfo(path);
            lock (_hashCache)
            {
                if (_hashCache.TryGetValue(path, out var c) && c.Time == info.LastWriteTimeUtc && c.Length == info.Length)
                    return c.Hash;
            }
            var hash = PluginService.HashFile(path);
            lock (_hashCache) _hashCache[path] = (info.LastWriteTimeUtc, info.Length, hash);
            return hash;
        }
        catch
        {
            return null;
        }
    }

    private static string Normalize(string name) =>
        new(name.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

    private static bool SameRepo(RemotePlugin? a, RemotePlugin b) =>
        a is not null && string.Equals(a.Repo.Name, b.Repo.Name, StringComparison.OrdinalIgnoreCase);

    /// <summary>Warns before installing a plugin into one scope when it is already active in the other.</summary>
    private async Task<bool> ConfirmDuplicateInstallAsync(RemotePlugin remote, PluginLocation location)
    {
        var others = Installed
            .Where(p => p.IsEnabled && p.Location.IsGlobal != location.IsGlobal && SameRepo(Status(p).Remote, remote))
            .ToList();
        if (others.Count == 0) return true;

        var message = location.IsGlobal
            ? $"{remote.Repo.Name} is already installed in these profiles:\n\n{string.Join("\n", others.Select(o => "  " + o.Location.Profile))}\n\n"
              + "The vatSys directory is loaded for every profile, so vatSys would load the plugin twice in those profiles. "
              + "Remove or disable the profile copies afterwards.\n\nInstall anyway?"
            : $"{remote.Repo.Name} is already installed in the vatSys directory:\n\n  {others[0].Path}\n\n"
              + $"That copy loads for every profile, so installing it into {location.Profile} as well makes vatSys load it twice.\n\nInstall anyway?";
        return await DialogService.ConfirmAsync("Plugin already installed", message, "Install anyway");
    }

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

    /// <summary>Removes one or more installed copies after a single confirmation listing every path.</summary>
    public async Task<bool> RemoveAsync(IReadOnlyList<InstalledPlugin> plugins)
    {
        if (plugins.Count == 0) return false;
        var where = Settings.UseRecycleBin
            ? "The files will be moved to the Recycle Bin."
            : "The files will be permanently deleted.";
        var list = string.Join("\n", plugins.Select(p => $"  {p.Location.Name}: {p.Path}"));
        var title = plugins.Count == 1 ? $"Remove \"{plugins[0].Name}\"?" : $"Remove {plugins.Count} copies of \"{plugins[0].Name}\"?";
        if (!await DialogService.ConfirmAsync("Remove plugin", $"{title}\n\n{list}\n\n{where}", "Remove", danger: true)) return false;
        if (!await ConfirmIfVatSysRunningAsync()) return false;

        foreach (var plugin in plugins)
        {
            try
            {
                await Task.Run(() => Plugins.Remove(plugin));
                Success($"Removed {plugin.Name} from {plugin.Location.Name}.");
            }
            catch (Exception ex)
            {
                Error($"Could not remove {plugin.Name} from {plugin.Location.Name}: {ex.Message}");
            }
        }
        await RescanAsync();
        return true;
    }

    public async Task<bool> InstallAsync(
        RemotePlugin remote, PluginLocation location, IProgress<double>? progress = null, bool skipRunningCheck = false)
    {
        // Existing copies in the same folder, whatever name they were installed under, are updated in place.
        var replace = Installed
            .Where(p => PluginService.SamePath(p.Location.Path, location.Path) && SameRepo(Status(p).Remote, remote))
            .ToList();
        if (replace.Count == 0 && !await ConfirmDuplicateInstallAsync(remote, location)) return false;
        if (!skipRunningCheck && !await ConfirmIfVatSysRunningAsync()) return false;

        var tag = remote.Release?.TagName ?? "?";
        Info($"Installing {remote.Repo.Name} {tag} to {location.Path} ...");
        try
        {
            var result = await Plugins.InstallAsync(remote, location, replace, progress);
            Success($"Installed {remote.Repo.Name} {tag} ({string.Join(", ", result.Record.Items)}) to {location.Name}."
                + (Settings.AutoUnblock ? " Files unblocked." : ""));
            foreach (var path in result.Replaced) Info($"Removed the old copy {path}.");
            result.Warnings.ForEach(Warn);
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
