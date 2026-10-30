using System.Diagnostics;
using System.IO.Compression;
using Microsoft.VisualBasic.FileIO;
using PluginsManager.Models;
using SearchOption = System.IO.SearchOption;

namespace PluginsManager.Services;

/// <summary>File-system side of plugin management: scanning, enabling, removing and installing.</summary>
public sealed class PluginService(SettingsStore store, GitHubService gitHub)
{
    public const string DisabledSuffix = ".disabled";

    private static readonly string[] DependencyPrefixes = ["Newtonsoft.", "System.", "Microsoft.", "NAudio", "Serilog"];

    private AppSettings Settings => store.Settings;

    public PluginLocation GlobalLocation =>
        new("vatSys (global)", Path.Combine(Settings.VatSysInstallDir, "bin", "Plugins"), true, null);

    public PluginLocation ProfileLocation(string profile) =>
        new($"Profile: {profile}", Path.Combine(Settings.VatSysFilesDir, "Profiles", profile, "Plugins"), false, profile);

    public IReadOnlyList<string> GetProfiles()
    {
        var dir = Path.Combine(Settings.VatSysFilesDir, "Profiles");
        if (!Directory.Exists(dir)) return [];
        return Directory.GetDirectories(dir)
            .Select(Path.GetFileName)
            .OfType<string>()
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public IReadOnlyList<PluginLocation> GetLocations() =>
        [GlobalLocation, .. GetProfiles().Select(ProfileLocation)];

    public List<InstalledPlugin> Scan(PluginLocation location)
    {
        var result = new List<InstalledPlugin>();
        if (!Directory.Exists(location.Path)) return result;

        foreach (var dir in Directory.GetDirectories(location.Path))
        {
            var files = Directory.GetFiles(dir, "*", SearchOption.AllDirectories);
            var enabled = files.Where(IsDll).ToList();
            var disabled = files.Where(IsDisabledDll).ToList();
            if (enabled.Count == 0 && disabled.Count == 0) continue; // config/data folder, not a plugin

            var name = Path.GetFileName(dir);
            result.Add(new InstalledPlugin
            {
                Name = name,
                ItemName = name,
                Path = dir,
                IsDirectory = true,
                Location = location,
                Version = ReadVersion(PickMainDll(name, enabled.Concat(disabled))),
                IsEnabled = enabled.Count > 0,
                BlockedFiles = files.Count(Unblocker.IsBlocked),
                Record = FindRecord(location, name),
            });
        }

        foreach (var file in Directory.GetFiles(location.Path))
        {
            var isDisabled = IsDisabledDll(file);
            if (!isDisabled && !IsDll(file)) continue;

            var itemName = Path.GetFileName(isDisabled ? file[..^DisabledSuffix.Length] : file);
            result.Add(new InstalledPlugin
            {
                Name = Path.GetFileNameWithoutExtension(itemName),
                ItemName = itemName,
                Path = file,
                IsDirectory = false,
                Location = location,
                Version = ReadVersion(file),
                IsEnabled = !isDisabled,
                BlockedFiles = Unblocker.IsBlocked(file) ? 1 : 0,
                Record = FindRecord(location, itemName),
            });
        }

        return result.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>Disables a plugin by renaming its DLLs to *.dll.disabled so vatSys skips them.</summary>
    public void SetEnabled(InstalledPlugin plugin, bool enable)
    {
        var files = plugin.IsDirectory
            ? Directory.GetFiles(plugin.Path, "*", SearchOption.AllDirectories)
            : [plugin.Path];

        foreach (var file in files)
        {
            if (enable && IsDisabledDll(file))
                File.Move(file, file[..^DisabledSuffix.Length], overwrite: true);
            else if (!enable && IsDll(file))
                File.Move(file, file + DisabledSuffix, overwrite: true);
        }
    }

    public void Remove(InstalledPlugin plugin)
    {
        var recycle = Settings.UseRecycleBin && OperatingSystem.IsWindows();
        if (plugin.IsDirectory)
        {
            if (recycle) FileSystem.DeleteDirectory(plugin.Path, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
            else Directory.Delete(plugin.Path, recursive: true);
        }
        else
        {
            if (recycle) FileSystem.DeleteFile(plugin.Path, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
            else File.Delete(plugin.Path);
        }

        if (plugin.Record is { } record)
        {
            record.Items.RemoveAll(i => string.Equals(i, plugin.ItemName, StringComparison.OrdinalIgnoreCase));
            if (record.Items.Count == 0) store.Installs.Remove(record);
            store.SaveInstalls();
        }
    }

    public int Unblock(InstalledPlugin plugin) => Unblocker.UnblockAll(plugin.Path);

    /// <summary>Runs the Unblock-File equivalent on every blocked DLL (enabled or disabled) in a plugin folder.</summary>
    public int UnblockDlls(PluginLocation location) =>
        Directory.Exists(location.Path)
            ? Directory.EnumerateFiles(location.Path, "*", SearchOption.AllDirectories)
                .Where(f => IsDll(f) || IsDisabledDll(f))
                .Count(f => Unblocker.IsBlocked(f) && Unblocker.Unblock(f))
            : 0;

    public async Task<InstallRecord> InstallAsync(
        RemotePlugin remote, PluginLocation location, IProgress<double>? progress, CancellationToken ct = default)
    {
        var release = remote.Release ?? throw new InvalidOperationException($"{remote.Repo.Name} has no release.");
        var asset = remote.Asset ?? throw new InvalidOperationException($"{remote.Repo.Name} {release.TagName} has no .zip or .dll asset.");

        var work = Path.Combine(Path.GetTempPath(), "vatSysPluginsManager", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        try
        {
            var download = Path.Combine(work, Path.GetFileName(asset.Name));
            await gitHub.DownloadAsync(asset.BrowserDownloadUrl, download, progress, ct);

            Directory.CreateDirectory(location.Path);
            var items = asset.IsZip
                ? InstallZip(download, Path.Combine(work, "extract"), location.Path, remote.Repo.Name)
                : [InstallDll(download, location.Path)];

            if (Settings.AutoUnblock)
                foreach (var item in items)
                    Unblocker.UnblockAll(Path.Combine(location.Path, item));

            var record = new InstallRecord
            {
                Repo = remote.Repo.Name,
                Tag = release.TagName,
                AssetName = asset.Name,
                LocationPath = location.Path,
                Items = items,
                InstalledAt = DateTimeOffset.Now,
            };
            store.Installs.RemoveAll(r =>
                string.Equals(r.Repo, record.Repo, StringComparison.OrdinalIgnoreCase) && SamePath(r.LocationPath, location.Path));
            store.Installs.Add(record);
            store.SaveInstalls();
            return record;
        }
        finally
        {
            try { Directory.Delete(work, recursive: true); } catch { /* temp cleanup is best effort */ }
        }
    }

    private static string InstallDll(string download, string pluginsDir)
    {
        var name = Path.GetFileName(download);
        var target = Path.Combine(pluginsDir, name);
        // Keep a disabled plugin disabled when it gets updated.
        if (File.Exists(target + DisabledSuffix) && !File.Exists(target)) target += DisabledSuffix;
        File.Copy(download, target, overwrite: true);
        return name;
    }

    private static List<string> InstallZip(string zip, string extractDir, string pluginsDir, string repoName)
    {
        ZipFile.ExtractToDirectory(zip, extractDir);
        var root = ResolveContentRoot(extractDir);

        var dirs = Directory.GetDirectories(root);
        var files = Directory.GetFiles(root);
        if (files.Length == 0 && dirs.Length > 0)
        {
            // One or more plugin folders: copy each one as-is.
            foreach (var dir in dirs) CopyPluginFolder(dir, Path.Combine(pluginsDir, Path.GetFileName(dir)));
            return dirs.Select(d => Path.GetFileName(d)).ToList();
        }

        // Loose files: give the plugin its own folder named after the repository.
        CopyPluginFolder(root, Path.Combine(pluginsDir, repoName));
        return [repoName];
    }

    /// <summary>Strips wrapper folders such as "bin/Plugins/" from release archives.</summary>
    private static string ResolveContentRoot(string dir)
    {
        var current = dir;
        for (var depth = 0; depth < 4; depth++)
        {
            var plugins = Path.Combine(current, "Plugins");
            if (Directory.Exists(plugins)) return plugins;

            var dirs = Directory.GetDirectories(current);
            if (Directory.GetFiles(current).Length > 0 || dirs.Length != 1) return current;

            var only = dirs[0];
            var leadsToPlugins = string.Equals(Path.GetFileName(only), "bin", StringComparison.OrdinalIgnoreCase)
                || Directory.Exists(Path.Combine(only, "Plugins"))
                || Directory.Exists(Path.Combine(only, "bin"));
            if (!leadsToPlugins) return current;
            current = only;
        }
        return current;
    }

    private static void CopyPluginFolder(string source, string target)
    {
        var wasDisabled = Directory.Exists(target)
            && Directory.EnumerateFiles(target, "*", SearchOption.AllDirectories).Any(IsDisabledDll)
            && !Directory.EnumerateFiles(target, "*", SearchOption.AllDirectories).Any(IsDll);

        foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
        {
            var dest = Path.Combine(target, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            File.Copy(file, dest, overwrite: true);
        }

        if (!wasDisabled) return;
        foreach (var dll in Directory.GetFiles(target, "*", SearchOption.AllDirectories).Where(IsDll))
            File.Move(dll, dll + DisabledSuffix, overwrite: true);
    }

    private InstallRecord? FindRecord(PluginLocation location, string itemName) =>
        store.Installs.FirstOrDefault(r =>
            SamePath(r.LocationPath, location.Path)
            && r.Items.Contains(itemName, StringComparer.OrdinalIgnoreCase));

    private static string? PickMainDll(string folderName, IEnumerable<string> dlls)
    {
        var list = dlls.ToList();
        var key = folderName.Replace(" ", "");
        return list.FirstOrDefault(d => Path.GetFileName(d).Replace(" ", "").Contains(key, StringComparison.OrdinalIgnoreCase))
            ?? list.FirstOrDefault(d => !DependencyPrefixes.Any(p => Path.GetFileName(d).StartsWith(p, StringComparison.OrdinalIgnoreCase)))
            ?? list.FirstOrDefault();
    }

    private static string? ReadVersion(string? file)
    {
        if (file is null) return null;
        try
        {
            var info = FileVersionInfo.GetVersionInfo(file);
            var v = info.ProductVersion ?? info.FileVersion;
            if (string.IsNullOrWhiteSpace(v)) return null;
            var plus = v.IndexOf('+'); // strip source-link commit hashes
            v = plus > 0 ? v[..plus] : v;
            return VersionUtil.Parse(v) == new Version(0, 0, 0, 0) ? null : v; // no real version stamped
        }
        catch
        {
            return null;
        }
    }

    public static bool IsDll(string path) => path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase);

    public static bool IsDisabledDll(string path) => path.EndsWith(".dll" + DisabledSuffix, StringComparison.OrdinalIgnoreCase);

    public static bool SamePath(string a, string b) =>
        string.Equals(Path.GetFullPath(a).TrimEnd('\\', '/'), Path.GetFullPath(b).TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase);
}
