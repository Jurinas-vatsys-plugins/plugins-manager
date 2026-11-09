using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;
using PluginsManager.Models;

namespace PluginsManager.Services;

public enum Distribution
{
    /// <summary>Local build: does not update itself.</summary>
    None,

    /// <summary>Self-contained single-file exe (release asset <see cref="SelfUpdater.ExeAsset"/>).</summary>
    Exe,

    /// <summary>Framework-dependent folder (release asset <see cref="SelfUpdater.ZipAsset"/>).</summary>
    Zip,
}

/// <summary>
/// Updates the manager in the format it was installed in: the single exe is replaced by the new exe, the
/// framework-dependent folder by the contents of the new zip. Windows does not let running files be overwritten,
/// but it lets them be renamed: replaced files become "*.old" and are deleted by the new version on startup.
/// </summary>
public static class SelfUpdater
{
    public const string ExeAsset = "vatSysPluginsManager.exe";
    public const string ZipAsset = "vatSysPluginsManager-framework-dependent.zip";
    private const string OldSuffix = ".old";
    private const string UpdateDirPrefix = ".update-";

    public static string? ExePath => Environment.ProcessPath;

    public static Distribution Current { get; } = DetectDistribution();

    public static bool IsSupported => OperatingSystem.IsWindows() && ExePath is not null && Current != Distribution.None;

    /// <summary>The asset matching the running format; each format only ever updates from its own asset.</summary>
    public static GhAsset? FindAsset(GhRelease release, Distribution distribution) =>
        distribution switch
        {
            Distribution.Exe => release.Assets.FirstOrDefault(a => Is(a, ExeAsset)),
            Distribution.Zip => release.Assets.FirstOrDefault(a => Is(a, ZipAsset)),
            _ => null,
        };

    private static bool Is(GhAsset asset, string name) => string.Equals(asset.Name, name, StringComparison.OrdinalIgnoreCase);

    private static Distribution DetectDistribution()
    {
        var value = Assembly.GetEntryAssembly()?
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(a => a.Key == "Distribution")?.Value;
        if (string.Equals(value, "exe", StringComparison.OrdinalIgnoreCase)) return Distribution.Exe;
        if (string.Equals(value, "zip", StringComparison.OrdinalIgnoreCase)) return Distribution.Zip;
        return Distribution.None;
    }

    /// <summary>Files renamed to *.old by the last update, deleted by the next start.</summary>
    private static string PendingCleanupFile => Path.Combine(SettingsStore.DataDir, "update-cleanup.txt");

    /// <summary>Deletes files replaced by the last update; they may stay locked for a moment while the old process exits.</summary>
    public static void CleanUpOldVersion()
    {
        if (!IsSupported || ExePath is not { } exe || Path.GetDirectoryName(exe) is not { } dir) return;

        // Only exact paths are deleted: the exe may live in a shared folder such as Downloads.
        var files = new List<string> { exe + OldSuffix, exe + ".download" }; // v0.2.1 renamed the exe without a list
        try
        {
            if (File.Exists(PendingCleanupFile)) files.AddRange(File.ReadAllLines(PendingCleanupFile).Where(l => l.Length > 0));
        }
        catch
        {
            // Nothing recorded.
        }

        _ = Task.Run(async () =>
        {
            for (var attempt = 0; attempt < 20; attempt++)
            {
                foreach (var file in files) TryDeleteFile(file);
                foreach (var update in Directory.GetDirectories(dir, UpdateDirPrefix + "*"))
                {
                    try { Directory.Delete(update, recursive: true); } catch { /* retried below */ }
                }

                // A file can belong to another install that is still running; keep the list until all are gone.
                if (!files.Any(File.Exists))
                {
                    TryDeleteFile(PendingCleanupFile);
                    return;
                }
                await Task.Delay(500);
            }
        });
    }

    public static Task InstallAsync(
        GitHubService gitHub, GhAsset asset, Distribution distribution, string exePath, IProgress<double>? progress) =>
        distribution switch
        {
            Distribution.Exe => InstallExeAsync(gitHub, asset, exePath, progress),
            Distribution.Zip => InstallZipAsync(gitHub, asset, exePath, progress),
            _ => throw new InvalidOperationException("This build cannot update itself."),
        };

    private static async Task InstallExeAsync(GitHubService gitHub, GhAsset asset, string exePath, IProgress<double>? progress)
    {
        var download = exePath + ".download";
        try
        {
            await gitHub.DownloadAsync(asset.BrowserDownloadUrl, download, progress);
            CheckSize(download, asset);
            if (!IsExecutable(download)) throw new IOException("Downloaded file is not a Windows executable.");
            Unblocker.Unblock(download);
            ReplaceFiles([(download, exePath)]);
        }
        finally
        {
            TryDeleteFile(download);
        }
    }

    private static async Task InstallZipAsync(GitHubService gitHub, GhAsset asset, string exePath, IProgress<double>? progress)
    {
        // Download and extract next to the app so the final moves stay on one volume.
        var appDir = Path.GetDirectoryName(exePath)!;
        var work = Path.Combine(appDir, UpdateDirPrefix + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        try
        {
            var zip = Path.Combine(work, ZipAsset);
            await gitHub.DownloadAsync(asset.BrowserDownloadUrl, zip, progress);
            CheckSize(zip, asset);

            var extract = Path.Combine(work, "files");
            ZipFile.ExtractToDirectory(zip, extract);
            var exeName = Path.GetFileName(exePath);
            if (!File.Exists(Path.Combine(extract, exeName)) || !File.Exists(Path.ChangeExtension(Path.Combine(extract, exeName), ".dll")))
                throw new IOException($"The update archive does not contain {exeName} and its DLL.");

            var moves = Directory.GetFiles(extract, "*", SearchOption.AllDirectories)
                .Select(f => (Source: f, Target: Path.Combine(appDir, Path.GetRelativePath(extract, f))))
                .ToList();
            foreach (var (source, _) in moves) Unblocker.Unblock(source);
            ReplaceFiles(moves);
        }
        finally
        {
            try { Directory.Delete(work, recursive: true); } catch { /* removed on next start */ }
        }
    }

    /// <summary>Moves each source over its target, renaming existing targets to *.old; rolls everything back on failure.</summary>
    private static void ReplaceFiles(IReadOnlyList<(string Source, string Target)> moves)
    {
        var done = new List<(string Target, string? Old)>();
        Directory.CreateDirectory(SettingsStore.DataDir);
        try
        {
            foreach (var (source, target) in moves)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                string? old = null;
                if (File.Exists(target))
                {
                    old = target + OldSuffix;
                    // A previous *.old may still be running (updated twice without a restart): use a fresh name.
                    if (File.Exists(old) && !TryDeleteFile(old)) old = $"{target}.{Guid.NewGuid():N}{OldSuffix}";
                    // Recorded before the rename, so even a crash halfway is cleaned up on the next start.
                    File.AppendAllLines(PendingCleanupFile, [old]);
                    File.Move(target, old);
                }
                done.Add((target, old));
                File.Move(source, target);
            }
        }
        catch
        {
            foreach (var (target, old) in Enumerable.Reverse(done))
            {
                try
                {
                    if (File.Exists(target)) File.Delete(target); // the new file, if it was moved in
                    if (old is not null) File.Move(old, target);
                }
                catch
                {
                    // Keep restoring the rest.
                }
            }
            throw;
        }
    }

    public static void Restart(string exePath)
    {
        var start = new ProcessStartInfo(exePath) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(exePath)! };
        foreach (var arg in SingleInstance.ReplacementArgs()) start.ArgumentList.Add(arg);
        Process.Start(start);
    }

    private static void CheckSize(string file, GhAsset asset)
    {
        var length = new FileInfo(file).Length;
        if (asset.Size > 0 && length != asset.Size)
            throw new IOException($"Downloaded file is {length:N0} bytes, expected {asset.Size:N0}.");
    }

    private static bool IsExecutable(string path)
    {
        using var stream = File.OpenRead(path);
        return stream.ReadByte() == 'M' && stream.ReadByte() == 'Z';
    }

    private static bool TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
