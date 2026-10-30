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
            var copies = _s.Installed.Where(p => _s.MatchRemote(p) == item.Model).ToList();
            item.InstalledText = copies.Count == 0
                ? "Not installed"
                : "Installed: " + string.Join(", ", copies.Select(c =>
                {
                    var version = VersionUtil.Display(AppServices.InstalledVersion(c));
                    return $"{c.Location.Name} ({(version.Length > 0 ? version : "?")})";
                }));
            item.IsInstalled = copies.Count > 0;
        }
    }

    [RelayCommand]
    private Task RefreshAsync() => _s.RefreshCatalogAsync();

    [RelayCommand]
    private void OpenOrganization() => DialogService.Open(GitHubService.OrgUrl);

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

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanInstall))]
    private bool _isBusy;

    [ObservableProperty] private double _progress;
    [ObservableProperty] private string _installedText = "Not installed";
    [ObservableProperty] private bool _isInstalled;

    [RelayCommand] private Task Install() => _owner.InstallAsync(this);
    [RelayCommand] private void OpenRepo() => DialogService.Open(Model.Repo.HtmlUrl);
    [RelayCommand] private void OpenRelease() => DialogService.Open(Model.Release?.HtmlUrl ?? Model.Repo.HtmlUrl + "/releases");

    private static string FormatSize(long bytes) =>
        bytes >= 1024 * 1024 ? $"{bytes / 1024d / 1024d:0.0} MB" : $"{Math.Max(1, bytes / 1024d):0} KB";
}
