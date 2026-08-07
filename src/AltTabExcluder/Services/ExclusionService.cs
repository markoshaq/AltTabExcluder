using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace AltTabExcluder.Services;

/// <summary>
/// Orchestrates Alt+Tab exclusion by combining <see cref="WindowManager"/> (pure
/// Win32 style read/write) with <see cref="ExclusionTracker"/> (which HWNDs we
/// excluded). This is the single entry point for all exclusion operations —
/// callers no longer touch <see cref="WindowManager"/> or
/// <see cref="ExclusionTracker"/> directly.
///
/// <para>
/// Owned by <c>Program</c> and injected into <see cref="RuleEngine"/>,
/// <see cref="TrayIconManager"/>, <see cref="WindowEnumerationService"/>, and
/// <c>TrayEventCoordinator</c>.
/// </para>
/// </summary>
public sealed class ExclusionService
{
    /// <summary>The underlying tracker — exposed for persistence and queries.</summary>
    public ExclusionTracker Tracker { get; }

    public ExclusionService(ExclusionTracker tracker)
    {
        Tracker = tracker;
    }

    /// <summary>
    /// Returns <c>true</c> when <paramref name="hwnd"/> is currently excluded from
    /// Alt+Tab (i.e. it carries the <c>WS_EX_TOOLWINDOW</c> style).
    /// </summary>
    public bool IsExcluded(IntPtr hwnd) => WindowManager.IsWindowExcluded(hwnd);

    /// <summary>
    /// Returns <c>true</c> if this window was excluded by AltTabExcluder (not just
    /// naturally carrying <c>WS_EX_TOOLWINDOW</c>).
    /// </summary>
    public bool WasExcludedByUs(IntPtr hwnd) => Tracker.Contains(hwnd);

    /// <summary>
    /// Toggles Alt+Tab visibility for <paramref name="hwnd"/>. When excluded, the
    /// window is restored to the switcher; when visible, it is hidden from it.
    /// </summary>
    public void Toggle(IntPtr hwnd)
    {
        bool nowExcluded = WindowManager.ToggleStyle(hwnd);
        if (nowExcluded)
            Tracker.Add(hwnd);
        else
            Tracker.Remove(hwnd);
    }

    /// <summary>Explicitly sets whether a window is excluded from Alt+Tab.</summary>
    public void SetExcluded(IntPtr hwnd, bool excluded)
    {
        WindowManager.SetStyle(hwnd, excluded);
        if (excluded)
            Tracker.Add(hwnd);
        else
            Tracker.Remove(hwnd);
    }

    /// <summary>
    /// Un-excludes every window that AltTabExcluder excluded. Natural tool
    /// windows (not in the tracker) are left untouched. Returns the number of
    /// windows restored.
    /// </summary>
    public int RestoreAll()
    {
        Tracker.PruneStale();
        var handles = Tracker.Handles.ToList();
        int count = 0;
        foreach (var hwnd in handles)
        {
            try
            {
                WindowManager.SetStyle(hwnd, excluded: false);
                count++;
            }
            catch (Exception ex)
            {
                AppLogger.LogWarning(ex, $"RestoreAll failed for HWND {hwnd}");
            }
        }
        return count;
    }

    /// <summary>
    /// Removes stale HWNDs from the tracking set. Call when the tray menu opens
    /// to prevent stale entries from accumulating.
    /// </summary>
    public void PruneStale() => Tracker.PruneStale();
}
