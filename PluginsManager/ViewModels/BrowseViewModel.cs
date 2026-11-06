using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PluginsManager.Models;
using PluginsManager.Services;

namespace PluginsManager.ViewModels;

public sealed partial class BrowseViewModel : ViewModelBase
{
    private readonly AppServices _s;

    public BrowseViewModel(AppServices services)
    {
        _s = services;
        _s.CatalogChanged += Rebuild;
        _s.InstalledChanged += UpdateInstalledState;
        _s.SettingsChanged += LoadTargetDefaults;
        LoadTargetDefaults();
    }

    public AppServices S => _s;

    public ObservableCollection<RemotePluginItem> Items { get; } = [];
    public ObservableCollection<string> Profiles { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TargetIsGlobal), nameof(TargetPathText))]
    private bool _targetIsProfile;


    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TargetPathText))]
    private string? _selectedProfile;

    [ObservableProperty] private bool _isEmpty;

    public bool TargetIsGlobal
    {
        get => !TargetIsProfile;
        set => TargetIsProfile = !value;
    }

    public string TargetPathText => Target()?.Path ?? "Select a profile";

    public PluginLocation? Target() =>
        TargetIsProfile
            ? SelectedProfile is { } p ? _s.Plugins.ProfileLocation(p) : null
            : _s.Plugins.GlobalLocation;

    private void LoadTargetDefaults()
    {
        Profiles.Clear();
        foreach (var p in _s.Plugins.GetProfiles()) Profiles.Add(p);

        TargetIsProfile = _s.Settings.DefaultTarget == InstallTarget.Profile && Profiles.Count > 0;
        SelectedProfile = Profiles.FirstOrDefault(p => p == _s.Settings.DefaultProfile) ?? Profiles.FirstOrDefault();
        OnPropertyChanged(nameof(TargetPathText));
    }

    private void Rebuild()
    {
        Items.Clear();
        foreach (var remote in _s.Catalog) Items.Add(new RemotePluginItem(remote, this));
        IsEmpty = Items.Count == 0;
        UpdateInstalledState();
    }

    private void UpdateInstalledState()
    {
        foreach (var item in Items)
        {
            var copies = _s.Installed
                .Where(p => string.Equals(_s.Status(p).Remote?.Repo.Name, item.Name, StringComparison.OrdinalIgnoreCase))
                .ToList();
            item.InstalledText = copies.Count == 0
                ? "Not installed"
                : "Installed: " + string.Join(", ", copies.Select(c =>
                {
                    var version = VersionUtil.Display(_s.Status(c).DisplayVersion);
                    return $"{c.Location.Name} ({(version.Length > 0 ? version : "?")})";
                }));
            item.Copies = copies;
            item.IsInstalled = copies.Count > 0;

            // Installed plugins only offer Update (every outdated copy) and Remove; Install is for new plugins.
            var outdated = copies.Where(c => _s.Status(c).HasUpdate).ToList();
            item.UpdateCount = outdated.Count;
            item.UpdateText = $"Update ({outdated.Count})";
            item.UpdateTip = "Update to " + item.TagText + ": "
                + string.Join(", ", outdated.Select(c => c.Location.Name).Distinct());
            item.RemoveTip = "Remove from: " + string.Join(", ", copies.Select(c => c.Location.Name));
        }
    }

    [RelayCommand]
    private Task RefreshAsync() => _s.RefreshCatalogAsync();

    [RelayCommand]
    private void OpenOrganization() => DialogService.Open(GitHubService.OrgUrl);

    [ObservableProperty] private bool _isInspectingZip;

    /// <summary>
    /// Installs a ZIP of an organisation plugin that was downloaded somewhere else. The ZIP is identified by its
    /// plugin DLLs; anything that is not one of the organisation's plugins is refused.
    /// </summary>
    [RelayCommand]
    private async Task InstallFromZipAsync()
    {
        if (Target() is not { } target)
        {
            await DialogService.AlertAsync("Install from ZIP", "Select a profile to install into first.");
            return;
        }
        if (await DialogService.PickFileAsync("Select a plugin ZIP", "ZIP archive", "*.zip") is not { } zip) return;

        AssetIndexEntry entry;
        bool isSource;
        IsInspectingZip = true;
        try
        {
            (entry, isSource) = await Task.Run(() => _s.Plugins.InspectZip(zip));
        }
        catch (Exception ex)
        {
            _s.Error($"Could not read {zip}: {ex.Message}");
            await DialogService.AlertAsync("Install from ZIP", $"The file could not be read as a ZIP archive:\n\n{ex.Message}");
            return;
        }
        finally
        {
            IsInspectingZip = false;
        }

        if (isSource)
        {
            await DialogService.AlertAsync("Source code archive",
                $"{Path.GetFileName(zip)} contains source code, not a built plugin. It has to be compiled before vatSys can load it.\n\n"
                + "Download the plugin's release ZIP instead, or install it from the Browse list.");
            return;
        }
        if (entry.Dlls.Count == 0)
        {
            await DialogService.AlertAsync("Install from ZIP", $"{Path.GetFileName(zip)} does not contain any plugin DLL.");
            return;
        }
        if (_s.IdentifyZip(entry) is not { } id)
        {
            await DialogService.AlertAsync("Not an organisation plugin",
                $"{Path.GetFileName(zip)} does not match any plugin of {GitHubService.Org}.\n\n"
                + "Only plugins from the organisation can be installed from a ZIP. If the plugin list is empty, click Refresh and try again.");
            return;
        }

        var latest = id.Remote.Release?.TagName ?? "?";
        var match = id.IsOfficial
            ? $"Its plugin DLLs are identical to the official release {latest}. Other files in the ZIP were not checked."
            : $"Its plugin DLLs are NOT identical to the latest official release {latest}. They may be from an older or newer version, modified, or not from the plugin author.";
        var disclaimer =
            "DISCLAIMER: THE SAFETY OF THE FILES IN THIS ZIP IS NOT GUARANTEED\n"
            + "This ZIP did not come from the official GitHub release download. The files inside it are not scanned or verified, "
            + "and neither vatSys Plugin Manager nor the plugin authors guarantee that they are safe, unmodified or working. "
            + "Plugins run inside vatSys with full access to your computer. Only continue if you trust where this file came from; "
            + "you install it at your own risk.\n\n"
            + $"Plugin: {id.Remote.Repo.Name}\n{match}\n\n"
            + $"File: {zip}\nInstalls: {string.Join(", ", entry.Items)}\nInto: {target.Path}";
        if (!await DialogService.ConfirmAsync("Install from ZIP: no safety guarantee", disclaimer, "I understand, install", danger: true)) return;

        await _s.InstallZipAsync(zip, id, target);
    }

    internal async Task UpdateAsync(RemotePluginItem item)
    {
        var locations = item.Copies
            .Where(c => _s.Status(c).HasUpdate)
            .Select(c => c.Location)
            .DistinctBy(l => l.Path, StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (locations.Count == 0 || !await _s.ConfirmIfVatSysRunningAsync()) return;

        item.IsBusy = true;
        try
        {
            foreach (var location in locations)
            {
                item.Progress = 0;
                await _s.InstallAsync(item.Model, location, new Progress<double>(p => item.Progress = p * 100), skipRunningCheck: true);
            }
        }
        finally
        {
            item.IsBusy = false;
        }
    }

    internal Task RemoveAsync(RemotePluginItem item) => _s.RemoveAsync(item.Copies);

    internal async Task InstallAsync(RemotePluginItem item)
    {
        if (Target() is not { } target)
        {
            _s.Warn("Select a profile to install into.");
            return;
        }

        item.IsBusy = true;
        item.Progress = 0;
        try
        {
            await _s.InstallAsync(item.Model, target, new Progress<double>(p => item.Progress = p * 100));
        }
        finally
        {
            item.IsBusy = false;
        }
    }
}

public sealed partial class RemotePluginItem : ObservableObject
{
    private readonly BrowseViewModel _owner;

    public RemotePluginItem(RemotePlugin model, BrowseViewModel owner)
    {
        Model = model;
        _owner = owner;
    }

    public RemotePlugin Model { get; }

    public string Name => Model.Repo.Name;
    public string Description => string.IsNullOrWhiteSpace(Model.Repo.Description) ? "No description provided." : Model.Repo.Description;
    public string TagText => Model.Release?.TagName ?? "no release";
    public bool IsPrerelease => Model.Release?.Prerelease == true;
    public string PublishedText => Model.Release?.PublishedAt is { } d ? $"released {d.LocalDateTime:yyyy-MM-dd}" : "";
    public bool HasError => Model.Error is not null;
    public string ErrorText => Model.Error ?? "";

    public string AssetText => Model.Asset is { } a
        ? $"{a.Name}  ·  {FormatSize(a.Size)}"
        : Model.Release is null ? "No release published yet" : "Release has no .zip or .dll asset";

    public bool CanInstall => Model.Asset is not null && !IsBusy;
    public bool ShowInstall => !IsInstalled && !IsBusy;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanInstall), nameof(ShowInstall))]
    private bool _isBusy;

    [ObservableProperty] private double _progress;
    [ObservableProperty] private string _installedText = "Not installed";
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowInstall))]
    private bool _isInstalled;
    [ObservableProperty] private string _updateText = "";
    [ObservableProperty] private string _updateTip = "";
    [ObservableProperty] private string _removeTip = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasUpdate))]
    private int _updateCount;

    public bool HasUpdate => UpdateCount > 0 && Model.Asset is not null;

    /// <summary>Installed copies of this plugin in all locations.</summary>
    public IReadOnlyList<InstalledPlugin> Copies { get; set; } = [];

    [RelayCommand] private Task Install() => _owner.InstallAsync(this);
    [RelayCommand] private Task Update() => _owner.UpdateAsync(this);
    [RelayCommand] private Task Remove() => _owner.RemoveAsync(this);
    [RelayCommand] private void OpenRepo() => DialogService.Open(Model.Repo.HtmlUrl);
    [RelayCommand] private void OpenRelease() => DialogService.Open(Model.Release?.HtmlUrl ?? Model.Repo.HtmlUrl + "/releases");

    private static string FormatSize(long bytes) =>
        bytes >= 1024 * 1024 ? $"{bytes / 1024d / 1024d:0.0} MB" : $"{Math.Max(1, bytes / 1024d):0} KB";
}
