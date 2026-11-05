using System.Diagnostics;
using PluginsManager.Models;

namespace PluginsManager.Services;

/// <summary>
/// Replaces the running single-file exe with the one from the latest release. Windows does not let a running exe be
/// overwritten, but it can be renamed: the old exe becomes "*.old" and is deleted by the new version on startup.
/// </summary>
public static class SelfUpdater
{
    public const string AssetName = "vatSysPluginsManager.exe";
    private const string OldSuffix = ".old";
    private const string DownloadSuffix = ".download";

    public static string? ExePath => Environment.ProcessPath;

    /// <summary>Only the published single-file exe updates itself; a dev build (exe + dll) is left alone.</summary>
    public static bool IsSupported =>
        OperatingSystem.IsWindows() && ExePath is { } exe && !File.Exists(Path.ChangeExtension(exe, ".dll"));

    public static GhAsset? FindAsset(GhRelease release) =>
        release.Assets.FirstOrDefault(a => string.Equals(a.Name, AssetName, StringComparison.OrdinalIgnoreCase));

    /// <summary>Deletes the previous exe left by an update; it may still be locked for a moment while it exits.</summary>
    public static void CleanUpOldVersion()
    {
        if (ExePath is not { } exe) return;
        var old = exe + OldSuffix;
        var download = exe + DownloadSuffix;
        if (!File.Exists(old) && !File.Exists(download)) return;

        _ = Task.Run(async () =>
        {
            for (var attempt = 0; attempt < 20; attempt++)
            {
                try
                {
                    if (File.Exists(old)) File.Delete(old);
                    if (File.Exists(download)) File.Delete(download);
                    return;
                }
                catch
                {
                    await Task.Delay(500);
                }
            }
        });
    }

    /// <summary>Downloads <paramref name="asset"/> and swaps it in for <paramref name="exePath"/>.</summary>
    public static async Task InstallAsync(GitHubService gitHub, GhAsset asset, string exePath, IProgress<double>? progress)
    {
        var download = exePath + DownloadSuffix;
        var old = exePath + OldSuffix;
        try
        {
            await gitHub.DownloadAsync(asset.BrowserDownloadUrl, download, progress);
            var length = new FileInfo(download).Length;
            if (asset.Size > 0 && length != asset.Size)
                throw new IOException($"Downloaded file is {length:N0} bytes, expected {asset.Size:N0}.");
            if (!IsExecutable(download))
                throw new IOException("Downloaded file is not a Windows executable.");
            Unblocker.Unblock(download);

            if (File.Exists(old)) File.Delete(old);
            File.Move(exePath, old);
            try
            {
                File.Move(download, exePath);
            }
            catch
            {
                File.Move(old, exePath); // put the current version back
                throw;
            }
        }
        finally
        {
            try { if (File.Exists(download)) File.Delete(download); } catch { /* best effort */ }
        }
    }

    public static void Restart(string exePath)
    {
        var start = new ProcessStartInfo(exePath) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(exePath)! };
        foreach (var arg in Environment.GetCommandLineArgs().Skip(1)) start.ArgumentList.Add(arg);
        Process.Start(start);
    }

    private static bool IsExecutable(string path)
    {
        using var stream = File.OpenRead(path);
        return stream.ReadByte() == 'M' && stream.ReadByte() == 'Z';
    }
}
