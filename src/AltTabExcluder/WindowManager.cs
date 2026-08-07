using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace AltTabExcluder;

/// <summary>
/// Pure Win32 window-style logic for excluding a window from the Alt+Tab switcher.
/// This class is <em>stateless</em> — all tracking of which HWNDs we excluded
/// lives in <see cref="Services.ExclusionTracker"/> / <see cref="Services.ExclusionService"/>.
///
/// <para>
/// Hiding from Alt+Tab is achieved by applying <see cref="WS_EX_TOOLWINDOW"/> and
/// stripping <see cref="WS_EX_APPWINDOW"/>. Showing again does the reverse. These
/// style changes take effect immediately for the shell's task switcher.
/// </para>
/// <para>
/// The style-bit computation (<see cref="ComputeExcludedStyle"/> /
/// <see cref="ComputeVisibleStyle"/>) is pure and unit-testable without any
/// Win32 interaction.
/// </para>
/// </summary>
public static class WindowManager
{
    // GWL_EXSTYLE index for GetWindowLongPtr / SetWindowLongPtr.
    private static readonly WINDOW_LONG_PTR_INDEX GWL_EXSTYLE = (WINDOW_LONG_PTR_INDEX)(-20);

    /// <summary>Extended window style: tool window (hidden from Alt+Tab/taskbar).</summary>
    public const uint WS_EX_TOOLWINDOW = 0x00000080;

    /// <summary>Extended window style: app window (forced onto the taskbar/Alt+Tab).</summary>
    public const uint WS_EX_APPWINDOW = 0x00040000;

    // ─── Pure style-bit math (no Win32 calls, unit-testable) ─────────────

    /// <summary>
    /// Computes the extended style that hides a window from Alt+Tab: adds
    /// <see cref="WS_EX_TOOLWINDOW"/>, strips <see cref="WS_EX_APPWINDOW"/>.
    /// </summary>
    public static uint ComputeExcludedStyle(uint currentStyle)
        => (currentStyle | WS_EX_TOOLWINDOW) & ~WS_EX_APPWINDOW;

    /// <summary>
    /// Computes the extended style that restores a window to Alt+Tab: strips
    /// <see cref="WS_EX_TOOLWINDOW"/>, adds <see cref="WS_EX_APPWINDOW"/>.
    /// </summary>
    public static uint ComputeVisibleStyle(uint currentStyle)
        => (currentStyle & ~WS_EX_TOOLWINDOW) | WS_EX_APPWINDOW;

    /// <summary>
    /// Returns <c>true</c> if <paramref name="style"/> carries the
    /// <see cref="WS_EX_TOOLWINDOW"/> bit (i.e. the window is excluded from
    /// Alt+Tab).
    /// </summary>
    public static bool IsStyleExcluded(uint style)
        => (style & WS_EX_TOOLWINDOW) != 0;

    // ─── Win32 style read/write ──────────────────────────────────────────

    /// <summary>
    /// Returns <c>true</c> when <paramref name="hwnd"/> is currently excluded from
    /// Alt+Tab (i.e. it carries the <see cref="WS_EX_TOOLWINDOW"/> style).
    /// </summary>
    public static bool IsWindowExcluded(IntPtr hwnd)
        => IsStyleExcluded(GetExtendedStyle(hwnd));

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
        bool isExcluded = IsStyleExcluded(ex);
        uint newStyle = isExcluded
            ? ComputeVisibleStyle(ex)
            : ComputeExcludedStyle(ex);
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
            ? ComputeExcludedStyle(ex)
            : ComputeVisibleStyle(ex);

        if (newStyle != ex)
            SetExtendedStyle(hwnd, newStyle);
    }
}
