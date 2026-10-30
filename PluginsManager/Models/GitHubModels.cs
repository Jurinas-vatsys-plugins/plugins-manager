using System.Text.Json.Serialization;

namespace PluginsManager.Models;

public sealed class GhRepo
{
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("description")] public string? Description { get; set; }
    [JsonPropertyName("html_url")] public string HtmlUrl { get; set; } = "";
    [JsonPropertyName("archived")] public bool Archived { get; set; }
    [JsonPropertyName("pushed_at")] public DateTimeOffset? PushedAt { get; set; }
}

public sealed class GhRelease
{
    [JsonPropertyName("tag_name")] public string TagName { get; set; } = "";
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("html_url")] public string HtmlUrl { get; set; } = "";
    [JsonPropertyName("draft")] public bool Draft { get; set; }
    [JsonPropertyName("prerelease")] public bool Prerelease { get; set; }
    [JsonPropertyName("published_at")] public DateTimeOffset? PublishedAt { get; set; }
    [JsonPropertyName("assets")] public List<GhAsset> Assets { get; set; } = [];
}

public sealed class GhAsset
{
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("size")] public long Size { get; set; }
    [JsonPropertyName("browser_download_url")] public string BrowserDownloadUrl { get; set; } = "";

    public bool IsZip => Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase);
    public bool IsDll => Name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase);
}

/// <summary>A repository of the plugin organisation together with its installable release.</summary>
public sealed class RemotePlugin
{
    public required GhRepo Repo { get; init; }
    public GhRelease? Release { get; init; }
    public string? Error { get; init; }

    /// <summary>The asset that will be installed: a zip is preferred over a bare DLL.</summary>
    public GhAsset? Asset =>
        Release?.Assets.FirstOrDefault(a => a.IsZip) ?? Release?.Assets.FirstOrDefault(a => a.IsDll);
}
