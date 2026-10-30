using System.Text.Json.Serialization;

namespace PluginsManager.Models;

/// <summary>Response of GET /v1/vatsys-plugins/plugins on the Jurina API.</summary>
public sealed class JrPluginList
{
    [JsonPropertyName("org")] public string Org { get; set; } = "";
    [JsonPropertyName("checked_at")] public DateTimeOffset? CheckedAt { get; set; }
    [JsonPropertyName("last_error")] public string? LastError { get; set; }
    [JsonPropertyName("plugins")] public List<JrPlugin> Plugins { get; set; } = [];
}

public sealed class JrPlugin
{
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("description")] public string? Description { get; set; }
    [JsonPropertyName("repo_url")] public string RepoUrl { get; set; } = "";
    [JsonPropertyName("latest")] public JrRelease? Latest { get; set; }

    /// <summary>Maps to the GitHub-shaped models the rest of the app works with; downloads keep pointing at GitHub.</summary>
    public RemotePlugin ToRemote() => new()
    {
        Repo = new GhRepo { Name = Name, Description = Description, HtmlUrl = RepoUrl },
        Release = Latest is { } r
            ? new GhRelease
            {
                TagName = r.Tag,
                Name = r.Name,
                HtmlUrl = string.IsNullOrEmpty(r.Url) ? RepoUrl + "/releases" : r.Url,
                PublishedAt = r.PublishedAt,
                Assets = r.Assets.Select(a => new GhAsset
                {
                    Name = a.Name,
                    Size = a.Size,
                    BrowserDownloadUrl = string.IsNullOrEmpty(a.GitHubUrl) ? a.DownloadUrl : a.GitHubUrl,
                }).ToList(),
            }
            : null,
    };
}

public sealed class JrRelease
{
    [JsonPropertyName("tag")] public string Tag { get; set; } = "";
    [JsonPropertyName("version")] public string? Version { get; set; }
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("published_at")] public DateTimeOffset? PublishedAt { get; set; }
    [JsonPropertyName("url")] public string Url { get; set; } = "";
    [JsonPropertyName("assets")] public List<JrAsset> Assets { get; set; } = [];
}

public sealed class JrAsset
{
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("size")] public long Size { get; set; }
    [JsonPropertyName("download_url")] public string DownloadUrl { get; set; } = "";
    [JsonPropertyName("github_url")] public string GitHubUrl { get; set; } = "";
}
