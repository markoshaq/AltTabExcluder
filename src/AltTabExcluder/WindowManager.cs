using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace AltTabExcluder;

/// <summary>
/// Win32 window-style read/write logic for excluding a window from the Alt+Tab
/// switcher. This class is <em>stateless</em> — all tracking of which HWNDs we
/// excluded lives in <see cref="Services.ExclusionTracker"/> /
/// <see cref="Services.ExclusionService"/>.
///
/// <para>
/// The pure style-bit math (<see cref="WindowStyleMath.ComputeExcludedStyle"/> /
/// <see cref="WindowStyleMath.ComputeVisibleStyle"/> / <see cref="WindowStyleMath.IsStyleExcluded"/>)
/// has been extracted into <see cref="WindowStyleMath"/> so it can be unit-tested
/// in isolation. This class handles only the Win32 interop (reading/writing
/// extended styles and broadcasting frame changes).
/// </para>
/// <para>
/// Hiding from Alt+Tab is achieved by applying <c>WS_EX_TOOLWINDOW</c> and
/// stripping <c>WS_EX_APPWINDOW</c>. Showing again does the reverse. These
/// style changes take effect immediately for the shell's task switcher.
/// </para>
/// </summary>
public static class WindowManager
{
    // GWL_EXSTYLE index for GetWindowLongPtr / SetWindowLongPtr.
    private static readonly WINDOW_LONG_PTR_INDEX GWL_EXSTYLE = (WINDOW_LONG_PTR_INDEX)(-20);

    // ─── Win32 style read/write ──────────────────────────────────────────

    /// <summary>
    /// Returns <c>true</c> when <paramref name="hwnd"/> is currently excluded from
    /// Alt+Tab (i.e. it carries the <c>WS_EX_TOOLWINDOW</c> style).
    /// </summary>
    public static bool IsWindowExcluded(IntPtr hwnd)
        => WindowStyleMath.IsStyleExcluded(GetExtendedStyle(hwnd));

    /// <summary>
    /// Reads the current extended window style of <paramref name="hwnd"/>.
    /// </summary>
    public static uint GetExtendedStyle(IntPtr hwnd)
    {
        // GetWindowLongPtr returns LONG_PTR (pointer-sized). The extended style is a
        // 32-bit value, so the upper bits are unused; cast down to uint.
        nint raw = PInvoke.GetWindowLongPtr((HWND)hwnd, GWL_EXSTYLE);
        return (uint)raw;
    }

    /// <summary>
    /// Applies the extended window style and broadcasts a frame change so the
    /// shell re-evaluates the window's taskbar/Alt+Tab presence immediately.
    /// </summary>
    public static void SetExtendedStyle(IntPtr hwnd, uint style)
    {
        PInvoke.SetWindowLongPtr((HWND)hwnd, GWL_EXSTYLE, (nint)style);

        // Broadcast a frame change so the shell re-evaluates the window's
        // taskbar/Alt+Tab presence. Without this, a newly excluded window can
        // linger on the taskbar until it is hidden/shown.
        PInvoke.SetWindowPos((HWND)hwnd, default, 0, 0, 0, 0,
            SET_WINDOW_POS_FLAGS.SWP_NOMOVE | SET_WINDOW_POS_FLAGS.SWP_NOSIZE |
            SET_WINDOW_POS_FLAGS.SWP_NOZORDER | SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE |
            SET_WINDOW_POS_FLAGS.SWP_FRAMECHANGED);
    }

    /// <summary>
    /// Toggles the Alt+Tab visibility style of <paramref name="hwnd"/>. Returns
    /// <c>true</c> if the window is now excluded, <c>false</c> if it is now visible.
    /// Does <em>not</em> update any tracking set — the caller is responsible for
    /// that (see <see cref="Services.ExclusionService.Toggle"/>).
    /// </summary>
    public static bool ToggleStyle(IntPtr hwnd)
    {
        uint ex = GetExtendedStyle(hwnd);
        bool isExcluded = WindowStyleMath.IsStyleExcluded(ex);
        uint newStyle = isExcluded
            ? WindowStyleMath.ComputeVisibleStyle(ex)
            : WindowStyleMath.ComputeExcludedStyle(ex);
        SetExtendedStyle(hwnd, newStyle);
        return !isExcluded; // now excluded if it wasn't before, and vice versa
    }

    /// <summary>
    /// Sets the Alt+Tab visibility style of <paramref name="hwnd"/> to excluded
    /// or visible. Does <em>not</em> update any tracking set — the caller is
    /// responsible for that (see <see cref="Services.ExclusionService.SetExcluded"/>).
    /// </summary>
    public static void SetStyle(IntPtr hwnd, bool excluded)
    {
        uint ex = GetExtendedStyle(hwnd);
        uint newStyle = excluded
            ? WindowStyleMath.ComputeExcludedStyle(ex)
            : WindowStyleMath.ComputeVisibleStyle(ex);

        if (newStyle != ex)
            SetExtendedStyle(hwnd, newStyle);
    }
}
