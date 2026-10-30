namespace PluginsManager.Models;

public enum InstallTarget
{
    VatSysDirectory,
    Profile,
}

public sealed class AppSettings
{
    public string VatSysInstallDir { get; set; } = DetectInstallDir();

    public string VatSysFilesDir { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "vatSys Files");

    public InstallTarget DefaultTarget { get; set; } = InstallTarget.Profile;

    public string? DefaultProfile { get; set; }

    public bool CheckForUpdatesOnStartup { get; set; } = true;

    public bool AutoUnblock { get; set; } = true;

    public bool UseRecycleBin { get; set; } = true;

    public AppSettings Clone() => (AppSettings)MemberwiseClone();

    private static string DetectInstallDir()
    {
        string[] candidates =
        [
            @"I:\vatSys",
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "vatSys"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "vatSys"),
        ];
        return candidates.FirstOrDefault(c => File.Exists(Path.Combine(c, "bin", "vatSys.exe"))) ?? candidates[0];
    }
}
