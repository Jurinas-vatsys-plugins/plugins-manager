using System.ComponentModel;
using System.Diagnostics;
using System.Security;
using System.Security.Principal;

namespace PluginsManager.Services;

/// <summary>Detects "access denied" failures and restarts the app as administrator when the user agrees.</summary>
public static class Elevation
{
    private const int AccessDeniedHResult = unchecked((int)0x80070005);
    private const int UacCancelled = 1223;

    public static bool IsElevated
    {
        get
        {
            if (!OperatingSystem.IsWindows()) return false;
            using var identity = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }
    }

    public static bool IsAccessDenied(Exception? ex)
    {
        for (; ex is not null; ex = ex.InnerException)
        {
            if (ex is UnauthorizedAccessException or SecurityException) return true;
            if (ex is IOException && ex.HResult == AccessDeniedHResult) return true;
        }
        return false;
    }

    /// <summary>Starts a new elevated instance. Returns false when the user cancelled the UAC prompt.</summary>
    public static bool RestartElevated()
    {
        var exe = Environment.ProcessPath ?? throw new InvalidOperationException("Unknown executable path.");
        var start = new ProcessStartInfo(exe)
        {
            UseShellExecute = true, // required for the "runas" verb
            Verb = "runas",
            WorkingDirectory = Path.GetDirectoryName(exe)!,
        };
        foreach (var arg in SingleInstance.ReplacementArgs()) start.ArgumentList.Add(arg);
        try
        {
            Process.Start(start);
            return true;
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == UacCancelled)
        {
            return false;
        }
    }
}
