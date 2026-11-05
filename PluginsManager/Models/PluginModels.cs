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

    /// <summary>Plugin DLLs (not shared dependencies) by file name with ".disabled" stripped, mapped to their full path.</summary>
    public IReadOnlyDictionary<string, string> PluginDlls { get; init; } = new Dictionary<string, string>();
}

public sealed class InstallResult
{
    public InstallRecord Record { get; set; } = new();

    /// <summary>Old copies that were removed because the release installs under a different name.</summary>
    public List<string> Replaced { get; } = [];

    public List<string> Warnings { get; } = [];
}

/// <summary>What a release asset installs, cached so installed plugins can be matched without downloading again.</summary>
public sealed class AssetIndexEntry
{
    public string Url { get; set; } = "";

    /// <summary>Top-level files/folders the asset creates in a Plugins folder.</summary>
    public List<string> Items { get; set; } = [];

    /// <summary>Lower-case DLL file name -> SHA-256 (hex) of that DLL in the release.</summary>
    public Dictionary<string, string> Dlls { get; set; } = [];
}

/// <summary>Result of comparing an installed plugin with the catalog and with the other plugin folders.</summary>
public sealed class PluginStatus
{
    public RemotePlugin? Remote { get; init; }
    public string? DisplayVersion { get; set; }
    public bool HasUpdate { get; set; }
    public bool IsLatest { get; set; }

    /// <summary>Enabled copies of the same plugin in the other scope (vatSys directory vs. a profile); vatSys loads both.</summary>
    public List<InstalledPlugin> Duplicates { get; } = [];
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
