namespace AltTabExcluder;

/// <summary>
/// Pure style-bit math for excluding a window from the Alt+Tab switcher.
/// This class is <em>stateless</em> and contains <em>no Win32 calls</em> —
/// it is fully unit-testable without a live desktop session.
///
/// <para>
/// Hiding from Alt+Tab is achieved by applying <see cref="WS_EX_TOOLWINDOW"/> and
/// stripping <see cref="WS_EX_APPWINDOW"/>. Showing again does the reverse.
/// </para>
/// <para>
/// <see cref="WindowManager"/> delegates the style computation to this class
/// and handles the actual Win32 read/write separately.
/// </para>
/// </summary>
public static class WindowStyleMath
{
    /// <summary>Extended window style: tool window (hidden from Alt+Tab/taskbar).</summary>
    public const uint WS_EX_TOOLWINDOW = 0x00000080;

    /// <summary>Extended window style: app window (forced onto the taskbar/Alt+Tab).</summary>
    public const uint WS_EX_APPWINDOW = 0x00040000;

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
}
