using System.Diagnostics;
using Windows.Win32;
using Windows.Win32.Foundation;

namespace AltTabExcluder.Services;

/// <summary>
/// Enumerates currently open, visible top-level windows via <c>EnumWindows</c>
/// and enriches each with process name and icon metadata.
/// </summary>
public static class WindowEnumerationService
{
    private const int MaxTitle = 512;

    // Class names we never want to show.
    private static readonly HashSet<string> IgnoredClasses = new(StringComparer.OrdinalIgnoreCase)
    {
        "Shell_TrayWnd",                     // Taskbar
        "Progman",                           // Desktop
        "WorkerW",                           // Desktop wallpaper host
        "MSCTFIME UI",                       // IME
        "IME",                               // IME
        "NotifyIconOverflowWindow",          // Tray overflow flyout (Win10)
        "TopLevelWindowForOverflowXamlIsland", // Tray overflow flyout (Win11 22H2+)
    };

    // Process names we never want to show. These are system shell components
    // that use generic window classes (e.g. Windows.UI.Core.CoreWindow) shared
    // by real UWP apps, so they can't be filtered by class name alone.
    private static readonly HashSet<string> IgnoredProcesses = new(StringComparer.OrdinalIgnoreCase)
    {
        "TextInputHost",   // Text input panel (emoji picker, handwriting, IME)
    };

    // Cache of process icons keyed by executable path. Avoids repeated
    // Icon.ExtractAssociatedIcon file reads every time the tray menu opens.
    // Icons are owned by the cache for the app lifetime — callers must not
    // dispose them. Memory cost is negligible (~1-4 KB per unique process).
    private static readonly Dictionary<string, Icon?> _iconCache = new(StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyList<WindowInfo> GetOpenWindows()
    {
        var results = new List<WindowInfo>();
        int currentPid = Environment.ProcessId;

        unsafe
        {
            PInvoke.EnumWindows((hwnd, lParam) =>
            {
                try
                {
                    AddIfRelevant(results, hwnd, currentPid);
                }
                catch
                {
                    // Never let a single bad window abort enumeration.
                }
                return true;
            }, (LPARAM)0);
        }

        // Stable, user-friendly ordering: by process name then title.
        results.Sort((a, b) =>
        {
            int c = string.Compare(a.ProcessName, b.ProcessName, StringComparison.OrdinalIgnoreCase);
            return c != 0 ? c : string.Compare(a.WindowTitle, b.WindowTitle, StringComparison.OrdinalIgnoreCase);
        });

        return results;
    }

    private static unsafe void AddIfRelevant(List<WindowInfo> results, HWND hwnd, int currentPid)
    {
        if (!PInvoke.IsWindowVisible(hwnd))
            return;

        // Skip shell windows: desktop, taskbar, IME, tray overflow flyout.
        char* cls = stackalloc char[256];
        int clsLen = PInvoke.GetClassName(hwnd, (PWSTR)cls, 256);
        string className = clsLen > 0 ? new string(cls, 0, clsLen) : string.Empty;
        if (IgnoredClasses.Contains(className))
            return;

        int titleLen = PInvoke.GetWindowTextLength(hwnd);
        if (titleLen <= 0)
            return;

        char* titleBuf = stackalloc char[MaxTitle];
        int written = PInvoke.GetWindowText(hwnd, (PWSTR)titleBuf, MaxTitle);
        string title = written > 0 ? new string(titleBuf, 0, written) : string.Empty;
        if (string.IsNullOrWhiteSpace(title))
            return;

        uint pid;
        PInvoke.GetWindowThreadProcessId(hwnd, &pid);
        if (pid == currentPid)
            return; // Don't list our own windows.

        string procName = "<unknown>";
        string? imagePath = null;
        try
        {
            using var proc = Process.GetProcessById((int)pid);
            procName = proc.ProcessName;
            try { imagePath = proc.MainModule?.FileName; }
            catch { /* elevated / inaccessible - leave path null */ }
        }
        catch
        {
            // Process may have exited between enumeration and lookup.
        }

        // Skip system shell processes that use generic window classes.
        if (IgnoredProcesses.Contains(procName))
            return;

        results.Add(new WindowInfo
        {
            Hwnd = (IntPtr)hwnd,
            ProcessId = pid,
            ProcessName = procName,
            WindowTitle = title,
            ProcessIcon = TryLoadIcon(imagePath),
            IsExcluded = WindowManager.IsWindowExcluded((IntPtr)hwnd),
            WasExcludedByUs = WindowManager.WasExcludedByUs((IntPtr)hwnd),
        });
    }

    /// <summary>
    /// Extracts a <see cref="System.Drawing.Icon"/> from the process's
    /// executable. Results are cached by <paramref name="imagePath"/> for the
    /// app lifetime — the returned icon is owned by the cache and must not be
    /// disposed by the caller.
    /// </summary>
    private static Icon? TryLoadIcon(string? imagePath)
    {
        if (string.IsNullOrEmpty(imagePath) || !File.Exists(imagePath))
            return null;

        // Fast path: serve from cache.
        if (_iconCache.TryGetValue(imagePath, out var cached))
            return cached;

        Icon? icon = null;
        try
        {
            icon = Icon.ExtractAssociatedIcon(imagePath);
            if (icon is null || icon.Handle == IntPtr.Zero)
                icon = null;
        }
        catch
        {
            icon = null;
        }

        _iconCache[imagePath] = icon;
        return icon;
    }
}
