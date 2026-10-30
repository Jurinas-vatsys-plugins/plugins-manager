using System.Net;
using System.Text.Json;
using PluginsManager.Models;

namespace PluginsManager.Services;

public sealed class GitHubException(string message) : Exception(message);

/// <summary>
/// Plugin metadata comes from the Jurina API (api.jurina.org), which caches the latest GitHub release of every
/// repository in the organisation, so refreshing never hits GitHub's rate limit. Files are still downloaded from GitHub.
/// </summary>
public sealed class GitHubService
{
    public const string Org = "Jurinas-vatsys-plugins";
    public const string ManagerRepo = "plugins-manager";
    public const string OrgUrl = $"https://github.com/orgs/{Org}/repositories";
    public const string ManagerUrl = $"https://github.com/{Org}/{ManagerRepo}";

    /// <summary>Organisation repositories that are not vatSys plugins: this manager and the website.</summary>
    public static readonly HashSet<string> NonPluginRepos = new(StringComparer.OrdinalIgnoreCase) { ManagerRepo, "website" };

    public const string ApiHost = "api.jurina.org";
    private const string Api = $"https://{ApiHost}/v1/vatsys-plugins";

    private readonly HttpClient _http;

    public GitHubService()
    {
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd($"vatSysPluginsManager/{AppInfo.Version}");
    }

    public async Task<JrPluginList> GetPluginsAsync(CancellationToken ct = default) =>
        await GetJsonAsync<JrPluginList>($"{Api}/plugins", ct) ?? new JrPluginList();

    /// <summary>Latest published release of one repository, or null when it has none (or does not exist).</summary>
    public async Task<GhRelease?> GetLatestReleaseAsync(string repo, CancellationToken ct = default) =>
        (await GetJsonAsync<JrPlugin>($"{Api}/plugins/{Uri.EscapeDataString(repo)}", ct))?.ToRemote().Release;

    public async Task DownloadAsync(string url, string destination, IProgress<double>? progress, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!response.IsSuccessStatusCode) throw await ToExceptionAsync(response);

        var total = response.Content.Headers.ContentLength;
        await using var source = await response.Content.ReadAsStreamAsync(ct);
        await using var target = File.Create(destination);
        var buffer = new byte[81920];
        long read = 0;
        int n;
        while ((n = await source.ReadAsync(buffer, ct)) > 0)
        {
            await target.WriteAsync(buffer.AsMemory(0, n), ct);
            read += n;
            if (total > 0) progress?.Report((double)read / total.Value);
        }
    }

    private async Task<T?> GetJsonAsync<T>(string url, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Accept.ParseAdd("application/json");

        using var response = await _http.SendAsync(request, ct);
        if (response.StatusCode == HttpStatusCode.NotFound) return default;
        if (!response.IsSuccessStatusCode) throw await ToExceptionAsync(response);

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        return await JsonSerializer.DeserializeAsync<T>(stream, cancellationToken: ct);
    }

    private static async Task<GitHubException> ToExceptionAsync(HttpResponseMessage response)
    {
        string? message = null;
        try
        {
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var root = doc.RootElement;
            message = root.TryGetProperty("error", out var error) // Jurina API: { "error": { "code", "message" } }
                ? error.GetProperty("message").GetString()
                : root.GetProperty("message").GetString(); // GitHub
        }
        catch
        {
            // Not a JSON error body.
        }

        var host = response.RequestMessage?.RequestUri?.Host ?? "server";
        return new GitHubException($"Request to {host} failed: {(int)response.StatusCode} {message ?? response.ReasonPhrase}");
    }
}
