using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using PluginsManager.Views;

namespace PluginsManager.Services;

public static class DialogService
{
    private static Window? MainWindow =>
        (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;

    public static async Task<bool> ConfirmAsync(string title, string message, string confirmText = "OK", bool danger = false)
    {
        if (MainWindow is not { } owner) return false;
        return await new ConfirmDialog(title, message, confirmText, danger).ShowDialog<bool>(owner);
    }

    public static async Task<string?> PickFolderAsync(string title, string? startPath)
    {
        if (MainWindow is not { } owner) return null;
        var options = new FolderPickerOpenOptions { Title = title, AllowMultiple = false };
        if (!string.IsNullOrWhiteSpace(startPath) && Directory.Exists(startPath))
            options.SuggestedStartLocation = await owner.StorageProvider.TryGetFolderFromPathAsync(startPath);
        var result = await owner.StorageProvider.OpenFolderPickerAsync(options);
        return result.Count > 0 ? result[0].TryGetLocalPath() : null;
    }

    public static void Open(string target)
    {
        try { Process.Start(new ProcessStartInfo(target) { UseShellExecute = true }); }
        catch { /* nothing sensible to do if the shell refuses */ }
    }

    /// <summary>Opens Explorer with the file or folder selected.</summary>
    public static void Reveal(string path)
    {
        if (File.Exists(path) || Directory.Exists(path))
            Process.Start("explorer.exe", $"/select,\"{path}\"");
        else if (Path.GetDirectoryName(path) is { } parent && Directory.Exists(parent))
            Open(parent);
    }
}
