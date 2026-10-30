using System.Text.Json;
using System.Text.Json.Serialization;
using PluginsManager.Models;

namespace PluginsManager.Services;

/// <summary>Persists settings and the install manifest under %AppData%\vatSysPluginsManager.</summary>
public sealed class SettingsStore
{
    public static string DataDir { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "vatSysPluginsManager");

    private static readonly string SettingsFile = Path.Combine(DataDir, "settings.json");
    private static readonly string InstallsFile = Path.Combine(DataDir, "installs.json");

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public AppSettings Settings { get; private set; } = new();

    public List<InstallRecord> Installs { get; private set; } = [];

    public void Load()
    {
        Settings = Read<AppSettings>(SettingsFile) ?? new AppSettings();
        Installs = Read<List<InstallRecord>>(InstallsFile) ?? [];
    }

    public void Save(AppSettings settings)
    {
        Settings = settings;
        Write(SettingsFile, Settings);
    }

    public void SaveInstalls() => Write(InstallsFile, Installs);

    private static T? Read<T>(string file)
    {
        try
        {
            return File.Exists(file) ? JsonSerializer.Deserialize<T>(File.ReadAllText(file), Json) : default;
        }
        catch
        {
            return default;
        }
    }

    private static void Write<T>(string file, T value)
    {
        Directory.CreateDirectory(DataDir);
        var tmp = file + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(value, Json));
        File.Move(tmp, file, overwrite: true);
    }
}
