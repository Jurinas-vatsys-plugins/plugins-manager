using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PluginsManager.Models;
using PluginsManager.Services;

namespace PluginsManager.ViewModels;

public sealed partial class SettingsViewModel : ViewModelBase
{
    private readonly AppServices _s;

    public SettingsViewModel(AppServices services)
    {
        _s = services;
        Load(_s.Settings);
    }

    public ObservableCollection<string> Profiles { get; } = [];
    public string DataDir => SettingsStore.DataDir;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(InstallDirStatus), nameof(InstallDirValid))]
    private string _vatSysInstallDir = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FilesDirStatus), nameof(FilesDirValid))]
    private string _vatSysFilesDir = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DefaultTargetIsGlobal))]
    private bool _defaultTargetIsProfile;

    [ObservableProperty] private string? _defaultProfile;
    [ObservableProperty] private bool _checkForUpdatesOnStartup;
    [ObservableProperty] private bool _autoUnblock;
    [ObservableProperty] private bool _useRecycleBin;

    public bool DefaultTargetIsGlobal
    {
        get => !DefaultTargetIsProfile;
        set => DefaultTargetIsProfile = !value;
    }

    public bool InstallDirValid => File.Exists(Path.Combine(VatSysInstallDir, "bin", "vatSys.exe"));

    public string InstallDirStatus => InstallDirValid
        ? $"vatSys.exe found · global plugins: {Path.Combine(VatSysInstallDir, "bin", "Plugins")}"
        : "bin\\vatSys.exe not found in this folder";

    public bool FilesDirValid => Directory.Exists(Path.Combine(VatSysFilesDir, "Profiles"));

    public string FilesDirStatus => FilesDirValid
        ? $"{Directory.GetDirectories(Path.Combine(VatSysFilesDir, "Profiles")).Length} profile(s) found"
        : "Profiles folder not found in this folder";

    partial void OnVatSysFilesDirChanged(string value) => LoadProfiles();

    private void Load(AppSettings s)
    {
        VatSysInstallDir = s.VatSysInstallDir;
        VatSysFilesDir = s.VatSysFilesDir;
        DefaultTargetIsProfile = s.DefaultTarget == InstallTarget.Profile;
        CheckForUpdatesOnStartup = s.CheckForUpdatesOnStartup;
        AutoUnblock = s.AutoUnblock;
        UseRecycleBin = s.UseRecycleBin;
        LoadProfiles();
        DefaultProfile = Profiles.Contains(s.DefaultProfile ?? "") ? s.DefaultProfile : Profiles.FirstOrDefault();
    }

    private void LoadProfiles()
    {
        var keep = DefaultProfile;
        Profiles.Clear();
        var dir = Path.Combine(VatSysFilesDir, "Profiles");
        if (Directory.Exists(dir))
            foreach (var p in Directory.GetDirectories(dir).Select(Path.GetFileName).OfType<string>().Order())
                Profiles.Add(p);
        DefaultProfile = keep is not null && Profiles.Contains(keep) ? keep : Profiles.FirstOrDefault();
    }

    [RelayCommand]
    private async Task BrowseInstallDirAsync()
    {
        if (await DialogService.PickFolderAsync("Select the vatSys installation folder", VatSysInstallDir) is { } path)
            VatSysInstallDir = path;
    }

    [RelayCommand]
    private async Task BrowseFilesDirAsync()
    {
        if (await DialogService.PickFolderAsync("Select the \"vatSys Files\" folder", VatSysFilesDir) is { } path)
            VatSysFilesDir = path;
    }

    [RelayCommand]
    private void Save()
    {
        _s.SaveSettings(new AppSettings
        {
            VatSysInstallDir = VatSysInstallDir.Trim(),
            VatSysFilesDir = VatSysFilesDir.Trim(),
            DefaultTarget = DefaultTargetIsProfile ? InstallTarget.Profile : InstallTarget.VatSysDirectory,
            DefaultProfile = DefaultProfile,
            CheckForUpdatesOnStartup = CheckForUpdatesOnStartup,
            AutoUnblock = AutoUnblock,
            UseRecycleBin = UseRecycleBin,
        });
    }

    [RelayCommand]
    private void Revert()
    {
        Load(_s.Settings);
        _s.Info("Settings reverted to the last saved values.");
    }

    [RelayCommand]
    private void ResetDefaults()
    {
        Load(new AppSettings());
        _s.Info("Default values loaded. Click Save to apply them.");
    }

    [RelayCommand]
    private void OpenDataDir()
    {
        Directory.CreateDirectory(DataDir);
        DialogService.Open(DataDir);
    }
}
