namespace PluginsManager.Models;

/// <summary>A folder vatSys loads plugins from.</summary>
public sealed record PluginLocation(string Name, string Path, bool IsGlobal, string? Profile)
{
    public override string ToString() => Name;
}

/// <summary>Tracks what the manager installed, so updates can be detected later.</summary>
public sealed class InstallRecord
{
    public string Repo { get; set; } = "";
    public string Tag { get; set; } = "";
    public string AssetName { get; set; } = "";
    public string LocationPath { get; set; } = "";
    public List<string> Items { get; set; } = [];
    public DateTimeOffset InstalledAt { get; set; }
}

public sealed class InstalledPlugin
{
    public required string Name { get; init; }
    public required string Path { get; init; }
    public required bool IsDirectory { get; init; }
    public required PluginLocation Location { get; init; }

    /// <summary>File or folder name with any ".disabled" suffix removed.</summary>
    public required string ItemName { get; init; }

    public string? Version { get; init; }
    public bool IsEnabled { get; init; }
    public int BlockedFiles { get; init; }
    public InstallRecord? Record { get; init; }
}

public enum LogLevel
{
    Info,
    Success,
    Warning,
    Error,
}

public sealed record LogEntry(DateTime Time, LogLevel Level, string Message)
{
    public string TimeText => Time.ToString("HH:mm:ss");
    public string LevelText => Level.ToString().ToUpperInvariant();
}
