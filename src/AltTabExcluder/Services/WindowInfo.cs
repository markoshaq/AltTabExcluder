using System.Drawing;

namespace AltTabExcluder.Services;

/// <summary>
/// A snapshot of a single open window produced by <see cref="WindowEnumerationService"/>.
/// </summary>
public sealed record WindowInfo
{
    public IntPtr Hwnd { get; init; }
    public uint ProcessId { get; init; }
    public string ProcessName { get; init; } = string.Empty;
    public string WindowTitle { get; init; } = string.Empty;

    /// <summary>WinForms-friendly icon for tray menu items. Owned by the
    /// icon cache in <see cref="WindowEnumerationService"/> — callers must
    /// NOT dispose this icon.</summary>
    public Icon? ProcessIcon { get; init; }

    /// <summary>True when the window currently carries the WS_EX_TOOLWINDOW style.</summary>
    public bool IsExcluded { get; init; }

    /// <summary>True when AltTabExcluder excluded this window (vs. it being a
    /// natural tool window). Only windows excluded by us can be un-excluded
    /// from Quick Exclude.</summary>
    public bool WasExcludedByUs { get; init; }
}
