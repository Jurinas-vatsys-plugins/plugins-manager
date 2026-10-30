using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PluginsManager.Models;
using PluginsManager.Services;

namespace PluginsManager.ViewModels;

public sealed partial class InstalledViewModel : ViewModelBase
{
    public const string AllLocations = "All locations";

    private readonly AppServices _s;
    private List<InstalledPluginItem> _all = [];

    public InstalledViewModel(AppServices services)
    {
        _s = services;
        _s.InstalledChanged += Rebuild;
        _s.CatalogChanged += Rebuild;
        _s.SettingsChanged += () => _ = RefreshAsync();
    }

    public ObservableCollection<InstalledPluginItem> Items { get; } = [];
    public ObservableCollection<string> LocationFilters { get; } = [AllLocations];

    [ObservableProperty] private string? _selectedLocationFilter = AllLocations;
    [ObservableProperty] private string _searchText = "";
    [ObservableProperty] private string _summaryText = "";
    [ObservableProperty] private bool _isEmpty = true;
    [ObservableProperty] private bool _isScanning;

    partial void OnSelectedLocationFilterChanged(string? value) => ApplyFilter();
    partial void OnSearchTextChanged(string value) => ApplyFilter();

    private void Rebuild()
    {
        var names = new[] { AllLocations }
            .Concat(_s.Installed.Select(p => p.Location.Name).Distinct())
            .ToList();
        if (!names.SequenceEqual(LocationFilters))
        {
            var selected = SelectedLocationFilter;
            LocationFilters.Clear();
            foreach (var n in names) LocationFilters.Add(n);
            SelectedLocationFilter = selected is not null && names.Contains(selected) ? selected : AllLocations;
        }

        _all = _s.Installed.Select(p => new InstalledPluginItem(p, _s.MatchRemote(p), this)).ToList();
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        var location = SelectedLocationFilter ?? AllLocations;
        var query = SearchText.Trim();
        var visible = _all.Where(i =>
                (location == AllLocations || i.LocationName == location)
                && (query.Length == 0
                    || i.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
                    || i.SourceText.Contains(query, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        Items.Clear();
        foreach (var item in visible) Items.Add(item);

        IsEmpty = Items.Count == 0;
        var disabled = visible.Count(i => !i.IsEnabled);
        var blocked = visible.Count(i => i.IsBlocked);
        var updates = visible.Count(i => i.HasUpdate);
        SummaryText = $"{visible.Count} plugin(s) · {disabled} disabled · {blocked} blocked · {updates} update(s)";
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        IsScanning = true;
        try { await _s.RescanAsync(); }
        finally { IsScanning = false; }
    }

    [RelayCommand]
    private void OpenLocation()
    {
        var location = _s.Installed.FirstOrDefault(p => p.Location.Name == SelectedLocationFilter)?.Location;
        DialogService.Open(location?.Path ?? _s.Settings.VatSysFilesDir);
    }

    [RelayCommand]
    private async Task UnblockAllAsync()
    {
        var blocked = Items.Where(i => i.IsBlocked).ToList();
        if (blocked.Count == 0)
        {
            _s.Info("No blocked plugin files found.");
            return;
        }

        var count = 0;
        foreach (var item in blocked)
        {
            try { count += _s.Plugins.Unblock(item.Model); }
            catch (Exception ex) { _s.Error($"Unblock of {item.Name} failed: {ex.Message}"); }
        }
        _s.Success($"Unblocked {count} file(s) in {blocked.Count} plugin(s).");
        await RefreshAsync();
    }

    internal async Task ToggleAsync(InstalledPluginItem item)
    {
        if (!await _s.ConfirmIfVatSysRunningAsync()) return;
        var enable = !item.IsEnabled;
        try
        {
            _s.Plugins.SetEnabled(item.Model, enable);
            _s.Success($"{(enable ? "Enabled" : "Disabled")} {item.Name} ({item.LocationName}).");
        }
        catch (Exception ex)
        {
            _s.Error($"Could not {(enable ? "enable" : "disable")} {item.Name}: {ex.Message}");
        }
        await RefreshAsync();
    }

    internal async Task RemoveAsync(InstalledPluginItem item)
    {
        var where = _s.Settings.UseRecycleBin
            ? "The files will be moved to the Recycle Bin."
            : "The files will be permanently deleted.";
        var confirmed = await DialogService.ConfirmAsync(
            "Remove plugin",
            $"Remove \"{item.Name}\" from {item.LocationName}?\n\n{item.Path}\n\n{where}",
            "Remove",
            danger: true);
        if (!confirmed || !await _s.ConfirmIfVatSysRunningAsync()) return;

        try
        {
            await Task.Run(() => _s.Plugins.Remove(item.Model));
            _s.Success($"Removed {item.Name} from {item.LocationName}.");
        }
        catch (Exception ex)
        {
            _s.Error($"Could not remove {item.Name}: {ex.Message}");
        }
        await RefreshAsync();
    }

    internal async Task UnblockAsync(InstalledPluginItem item)
    {
        try
        {
            var count = _s.Plugins.Unblock(item.Model);
            _s.Success($"Unblocked {count} file(s) in {item.Name}.");
        }
        catch (Exception ex)
        {
            _s.Error($"Unblock of {item.Name} failed: {ex.Message}");
        }
        await RefreshAsync();
    }

    internal Task UpdateAsync(InstalledPluginItem item) =>
        item.Remote is { } remote ? _s.InstallAsync(remote, item.Model.Location) : Task.CompletedTask;
}

public sealed partial class InstalledPluginItem : ObservableObject
{
    private readonly InstalledViewModel _owner;

    public InstalledPluginItem(InstalledPlugin model, RemotePlugin? remote, InstalledViewModel owner)
    {
        Model = model;
        Remote = remote;
        _owner = owner;

        var installed = AppServices.InstalledVersion(model);
        var latest = remote?.Release?.TagName;
        HasUpdate = remote?.Asset is not null && VersionUtil.IsNewer(latest, installed);
        UpdateText = HasUpdate ? $"Update to {latest}" : "";
    }

    public InstalledPlugin Model { get; }
    public RemotePlugin? Remote { get; }

    public string Name => Model.Name;
    public string Path => Model.Path;
    public string LocationName => Model.Location.Name;
    public bool IsEnabled => Model.IsEnabled;
    public bool IsBlocked => Model.BlockedFiles > 0;
    public bool HasUpdate { get; }
    public string UpdateText { get; }

    public string DetailText => Model.IsDirectory ? $"{Model.ItemName}\\" : Model.ItemName;
    public string KindText => Model.IsDirectory ? "FOLDER" : "DLL";
    public string StatusText => IsEnabled ? "Enabled" : "Disabled";
    public string ToggleText => IsEnabled ? "Disable" : "Enable";
    public string ToggleGlyph => IsEnabled ? "\uE769" : "\uE768";
    public string BlockedText => $"BLOCKED ({Model.BlockedFiles})";
    public string VersionText => VersionUtil.Display(AppServices.InstalledVersion(Model)) is { Length: > 0 } v ? v : "—";

    public string SourceText => Remote is not null
        ? $"github/{Remote.Repo.Name}"
        : Model.Record is not null ? $"github/{Model.Record.Repo}" : "local";

    [RelayCommand] private Task Toggle() => _owner.ToggleAsync(this);
    [RelayCommand] private Task Remove() => _owner.RemoveAsync(this);
    [RelayCommand] private Task Unblock() => _owner.UnblockAsync(this);
    [RelayCommand] private Task Update() => _owner.UpdateAsync(this);
    [RelayCommand] private void Reveal() => DialogService.Reveal(Path);
}
