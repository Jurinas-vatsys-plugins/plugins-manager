using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace PluginsManager.Services;

public static class AppInfo
{
    public static string Version { get; } = ReadVersion();

    private static string ReadVersion()
    {
        var v = Assembly.GetEntryAssembly()?.GetName().Version ?? new Version(0, 0, 0);
        return $"{v.Major}.{v.Minor}.{Math.Max(v.Build, 0)}";
    }
}

public static partial class VersionUtil
{
    [GeneratedRegex(@"^\d+(\.\d+){0,3}")]
    private static partial Regex Numeric();

    public static Version? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var m = Numeric().Match(text.Trim().TrimStart('v', 'V'));
        if (!m.Success) return null;
        var s = m.Value.Contains('.') ? m.Value : m.Value + ".0";
        if (!Version.TryParse(s, out var v)) return null;
        return new Version(v.Major, v.Minor, Math.Max(v.Build, 0), Math.Max(v.Revision, 0));
    }

    /// <summary>True when <paramref name="latest"/> is a different (newer, if comparable) version than <paramref name="current"/>.</summary>
    public static bool IsNewer(string? latest, string? current)
    {
        if (string.IsNullOrWhiteSpace(latest) || string.IsNullOrWhiteSpace(current)) return false;
        var a = Parse(latest);
        var b = Parse(current);
        if (a is not null && b is not null) return a > b;
        return !string.Equals(latest.Trim(), current.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    public static string Display(string? text) =>
        Parse(text) is { } v ? (v.Revision > 0 ? v.ToString(4) : v.ToString(3)) : text ?? "";
}

/// <summary>
/// Equivalent of PowerShell's Unblock-File: removes the "Zone.Identifier" alternate data stream
/// that Windows attaches to downloaded files and that stops .NET from loading plugin DLLs.
/// </summary>
public static partial class Unblocker
{
    private const string ZoneStream = ":Zone.Identifier";
    private const uint InvalidFileAttributes = 0xFFFFFFFF;

    [LibraryImport("kernel32.dll", EntryPoint = "DeleteFileW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DeleteFile(string path);

    [LibraryImport("kernel32.dll", EntryPoint = "GetFileAttributesW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    private static partial uint GetFileAttributes(string path);

    public static bool IsBlocked(string file) =>
        OperatingSystem.IsWindows() && GetFileAttributes(file + ZoneStream) != InvalidFileAttributes;

    public static bool Unblock(string file) =>
        OperatingSystem.IsWindows() && DeleteFile(file + ZoneStream);

    public static int CountBlocked(string path) => Files(path).Count(IsBlocked);

    public static int UnblockAll(string path) => Files(path).Count(Unblock);

    private static IEnumerable<string> Files(string path) =>
        Directory.Exists(path) ? Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories)
        : File.Exists(path) ? [path]
        : [];
}
