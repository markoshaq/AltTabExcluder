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

    /// <summary>
    /// Enumerates all open visible top-level windows, enriched with process
    /// name, icon, and exclusion state.
    /// </summary>
    /// <param name="exclusion">
    /// The exclusion service used to populate <see cref="WindowInfo.IsExcluded"/>
    /// and <see cref="WindowInfo.WasExcludedByUs"/>. Pass <c>null</c> to skip
    /// exclusion-state population (both fields default to <c>false</c>).
    /// </param>
    public static IReadOnlyList<WindowInfo> GetOpenWindows(ExclusionService? exclusion = null)
    {
        var results = new List<WindowInfo>();
        int currentPid = Environment.ProcessId;

        unsafe
        {
            PInvoke.EnumWindows((hwnd, lParam) =>
            {
                try
                {
                    AddIfRelevant(results, hwnd, currentPid, exclusion);
                }
                catch (Exception ex)
                {
                    // Never let a single bad window abort enumeration.
                    AppLogger.LogWarning(ex, "EnumWindows callback failed for a window");
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

    private static unsafe void AddIfRelevant(
        List<WindowInfo> results, HWND hwnd, int currentPid, ExclusionService? exclusion)
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
            catch (Exception ex) { AppLogger.LogDebug($"Could not read MainModule for PID {pid}: {ex.Message}"); }
        }
        catch (Exception ex)
        {
            // Process may have exited between enumeration and lookup.
            AppLogger.LogDebug($"Process lookup failed for PID {pid}: {ex.Message}");
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
            IsExcluded = exclusion?.IsExcluded((IntPtr)hwnd) ?? false,
            WasExcludedByUs = exclusion?.WasExcludedByUs((IntPtr)hwnd) ?? false,
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
        catch (Exception ex)
        {
            AppLogger.LogDebug($"Icon extraction failed for {imagePath}: {ex.Message}");
            icon = null;
        }

        _iconCache[imagePath] = icon;
        return icon;
    }
}
