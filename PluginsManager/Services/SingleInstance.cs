using System.Diagnostics;

namespace PluginsManager.Services;

/// <summary>
/// Keeps one manager running per user session. A copy started by an update or an elevated restart replaces the
/// process that started it (waiting for it to exit, killing it if it hangs); any other second launch only brings
/// the running window to the front and exits.
/// </summary>
public static class SingleInstance
{
    /// <summary>Command-line flag carrying the id of the process being replaced.</summary>
    public const string ReplacesArg = "--replaces-pid";

    private const string Name = "vatSysPluginsManager.SingleInstance";
    private static Mutex? _mutex;
    private static EventWaitHandle? _activate;

    /// <summary>Returns false when another instance already runs; the caller should then exit.</summary>
    public static bool Acquire(string[] args)
    {
        var replaced = ReplacedProcessId(args);
        if (replaced is { } pid) WaitForExitOrKill(pid);

        try
        {
            _activate = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\" + Name + ".Activate");
            _mutex = new Mutex(false, @"Local\" + Name);
        }
        catch (UnauthorizedAccessException)
        {
            return false; // created by an instance running as administrator
        }

        try
        {
            // The replaced process may still hold the mutex for a moment while it exits.
            if (_mutex.WaitOne(replaced is null ? TimeSpan.Zero : TimeSpan.FromSeconds(10))) return true;
        }
        catch (AbandonedMutexException)
        {
            return true; // previous owner died without releasing; we own it now
        }

        _activate.Set(); // ask the running instance to show its window
        return false;
    }

    /// <summary>Calls <paramref name="onActivate"/> whenever a second launch asks this instance to come forward.</summary>
    public static void ListenForActivation(Action onActivate)
    {
        if (_activate is not { } handle) return;
        new Thread(() =>
        {
            while (handle.WaitOne()) onActivate();
        }) { IsBackground = true, Name = "SingleInstance.Activate" }.Start();
    }

    /// <summary>Arguments for a process that replaces this one: the current ones plus this process id.</summary>
    public static IEnumerable<string> ReplacementArgs()
    {
        var args = Environment.GetCommandLineArgs().Skip(1).ToList();
        var i = args.IndexOf(ReplacesArg);
        if (i >= 0) args.RemoveRange(i, Math.Min(2, args.Count - i));
        args.Add(ReplacesArg);
        args.Add(Environment.ProcessId.ToString());
        return args;
    }

    private static int? ReplacedProcessId(string[] args)
    {
        var i = Array.IndexOf(args, ReplacesArg);
        return i >= 0 && i + 1 < args.Length && int.TryParse(args[i + 1], out var pid) ? pid : null;
    }

    private static void WaitForExitOrKill(int pid)
    {
        try
        {
            using var old = Process.GetProcessById(pid);
            if (old.WaitForExit(5000)) return;
            old.Kill(entireProcessTree: true);
            old.WaitForExit(5000);
        }
        catch
        {
            // Already gone, or not ours to touch.
        }
    }
}
