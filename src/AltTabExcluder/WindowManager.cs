using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace AltTabExcluder;

/// <summary>
/// Core Win32 window-style logic for excluding a window from the Alt+Tab switcher.
///
/// Hiding from Alt+Tab is achieved by applying <see cref="WS_EX_TOOLWINDOW"/> and
/// stripping <see cref="WS_EX_APPWINDOW"/>. Showing again does the reverse. These
/// style changes take effect immediately for the shell's task switcher.
/// </summary>
public static class WindowManager
{
    // GWL_EXSTYLE index for GetWindowLongPtr / SetWindowLongPtr.
    private static readonly WINDOW_LONG_PTR_INDEX GWL_EXSTYLE = (WINDOW_LONG_PTR_INDEX)(-20);

    /// <summary>Extended window style: tool window (hidden from Alt+Tab/taskbar).</summary>
    public const uint WS_EX_TOOLWINDOW = 0x00000080;

    /// <summary>Extended window style: app window (forced onto the taskbar/Alt+Tab).</summary>
    public const uint WS_EX_APPWINDOW = 0x00040000;

    /// <summary>
    /// Tracks HWNDs that AltTabExcluder has excluded. Used to distinguish windows
    /// we excluded from windows that naturally have WS_EX_TOOLWINDOW (tool palettes,
    /// helper windows, etc.) — only the former can be un-excluded by the user.
    /// Populated from persisted settings on startup.
    /// </summary>
    private static readonly HashSet<IntPtr> ExcludedByUs = new();

    /// <summary>Loads persisted excluded-by-us HWNDs into the tracking set.</summary>
    public static void LoadExcludedByUs(IEnumerable<long> handles)
    {
        foreach (long h in handles)
            ExcludedByUs.Add((IntPtr)h);
    }

    /// <summary>Returns the current set of excluded-by-us HWNDs for persistence.</summary>
    public static IEnumerable<long> GetExcludedByUsForSave()
        => ExcludedByUs.Select(h => h.ToInt64());

    /// <summary>
    /// Removes HWNDs from the tracking set that are no longer valid windows.
    /// Call this periodically (e.g. when the tray menu opens) to prevent stale
    /// entries from accumulating and to avoid false positives from HWND recycling.
    /// </summary>
    public static void PruneStaleHandles()
    {
        ExcludedByUs.RemoveWhere(h => !PInvoke.IsWindow((HWND)h));
    }

    /// <summary>
    /// Returns <c>true</c> when <paramref name="hwnd"/> is currently excluded from
    /// Alt+Tab (i.e. it carries the <see cref="WS_EX_TOOLWINDOW"/> style).
    /// </summary>
    public static bool IsWindowExcluded(IntPtr hwnd)
    {
        uint ex = GetExtendedStyle(hwnd);
        return (ex & WS_EX_TOOLWINDOW) != 0;
    }

    /// <summary>
    /// Returns <c>true</c> if this window was excluded by AltTabExcluder (not just
    /// naturally carrying WS_EX_TOOLWINDOW).
    /// </summary>
    public static bool WasExcludedByUs(IntPtr hwnd)
        => ExcludedByUs.Contains(hwnd);

    /// <summary>
    /// Toggles Alt+Tab visibility for <paramref name="hwnd"/>. When excluded, the
    /// window is restored to the switcher; when visible, it is hidden from it.
    /// </summary>
    public static void ToggleAltTabVisibility(IntPtr hwnd)
    {
        uint ex = GetExtendedStyle(hwnd);
        uint newStyle = IsWindowExcluded(hwnd)
            ? (ex & ~WS_EX_TOOLWINDOW) | WS_EX_APPWINDOW   // show again
            : (ex | WS_EX_TOOLWINDOW) & ~WS_EX_APPWINDOW;  // hide

        SetExtendedStyle(hwnd, newStyle);

        if (IsWindowExcluded(hwnd))
            ExcludedByUs.Add(hwnd);
        else
            ExcludedByUs.Remove(hwnd);
    }

    /// <summary>Explicitly sets whether a window is excluded from Alt+Tab.</summary>
    public static void SetExcluded(IntPtr hwnd, bool excluded)
    {
        uint ex = GetExtendedStyle(hwnd);
        uint newStyle = excluded
            ? (ex | WS_EX_TOOLWINDOW) & ~WS_EX_APPWINDOW
            : (ex & ~WS_EX_TOOLWINDOW) | WS_EX_APPWINDOW;

        if (newStyle != ex)
            SetExtendedStyle(hwnd, newStyle);

        if (excluded)
            ExcludedByUs.Add(hwnd);
        else
            ExcludedByUs.Remove(hwnd);
    }

    private static uint GetExtendedStyle(IntPtr hwnd)
    {
        // GetWindowLongPtr returns LONG_PTR (pointer-sized). The extended style is a
        // 32-bit value, so the upper bits are unused; cast down to uint.
        nint raw = PInvoke.GetWindowLongPtr((HWND)hwnd, GWL_EXSTYLE);
        return (uint)raw;
    }

    private static void SetExtendedStyle(IntPtr hwnd, uint style)
    {
        // Apply the new extended style.
        PInvoke.SetWindowLongPtr((HWND)hwnd, GWL_EXSTYLE, (nint)style);

        // Broadcast a frame change so the shell re-evaluates the window's
        // taskbar/Alt+Tab presence. Without this, a newly excluded window can
        // linger on the taskbar until it is hidden/shown.
        PInvoke.SetWindowPos((HWND)hwnd, default, 0, 0, 0, 0,
            SET_WINDOW_POS_FLAGS.SWP_NOMOVE | SET_WINDOW_POS_FLAGS.SWP_NOSIZE |
            SET_WINDOW_POS_FLAGS.SWP_NOZORDER | SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE |
            SET_WINDOW_POS_FLAGS.SWP_FRAMECHANGED);
    }
}
