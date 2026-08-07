using AltTabExcluder;

namespace AltTabExcluder.Tests;

/// <summary>
/// Tests for the pure style-bit math in <see cref="WindowManager"/>.
/// These don't touch Win32 — they verify the bit manipulation logic that
/// determines whether a window is hidden from or shown in Alt+Tab.
/// </summary>
public class WindowManagerStyleMathTests
{
    // ─── ComputeExcludedStyle ───────────────────────────────────────────

    [Fact]
    public void ComputeExcludedStyle_AddsToolWindow_StripsAppWindow()
    {
        uint style = 0;
        uint result = WindowManager.ComputeExcludedStyle(style);

        Assert.True((result & WindowManager.WS_EX_TOOLWINDOW) != 0,
            "WS_EX_TOOLWINDOW should be set");
        Assert.True((result & WindowManager.WS_EX_APPWINDOW) == 0,
            "WS_EX_APPWINDOW should be cleared");
    }

    [Fact]
    public void ComputeExcludedStyle_PreservesOtherBits()
    {
        // WS_EX_LAYERED = 0x00080000, WS_EX_TOPMOST = 0x00000008
        uint otherBits = 0x00080000 | 0x00000008;
        uint result = WindowManager.ComputeExcludedStyle(otherBits);

        Assert.True((result & 0x00080000) != 0, "WS_EX_LAYERED should be preserved");
        Assert.True((result & 0x00000008) != 0, "WS_EX_TOPMOST should be preserved");
        Assert.True((result & WindowManager.WS_EX_TOOLWINDOW) != 0,
            "WS_EX_TOOLWINDOW should be set");
    }

    [Fact]
    public void ComputeExcludedStyle_StripsAppWindowEvenIfAlreadySet()
    {
        uint style = WindowManager.WS_EX_APPWINDOW;
        uint result = WindowManager.ComputeExcludedStyle(style);

        Assert.True((result & WindowManager.WS_EX_APPWINDOW) == 0,
            "WS_EX_APPWINDOW should be cleared even if it was set");
        Assert.True((result & WindowManager.WS_EX_TOOLWINDOW) != 0,
            "WS_EX_TOOLWINDOW should be set");
    }

    [Fact]
    public void ComputeExcludedStyle_IsIdempotent()
    {
        uint style = 0;
        uint once = WindowManager.ComputeExcludedStyle(style);
        uint twice = WindowManager.ComputeExcludedStyle(once);

        Assert.Equal(once, twice);
    }

    // ─── ComputeVisibleStyle ────────────────────────────────────────────

    [Fact]
    public void ComputeVisibleStyle_StripsToolWindow_AddsAppWindow()
    {
        uint style = WindowManager.WS_EX_TOOLWINDOW;
        uint result = WindowManager.ComputeVisibleStyle(style);

        Assert.True((result & WindowManager.WS_EX_TOOLWINDOW) == 0,
            "WS_EX_TOOLWINDOW should be cleared");
        Assert.True((result & WindowManager.WS_EX_APPWINDOW) != 0,
            "WS_EX_APPWINDOW should be set");
    }

    [Fact]
    public void ComputeVisibleStyle_PreservesOtherBits()
    {
        uint otherBits = 0x00080000 | WindowManager.WS_EX_TOOLWINDOW;
        uint result = WindowManager.ComputeVisibleStyle(otherBits);

        Assert.True((result & 0x00080000) != 0, "WS_EX_LAYERED should be preserved");
        Assert.True((result & WindowManager.WS_EX_TOOLWINDOW) == 0,
            "WS_EX_TOOLWINDOW should be cleared");
        Assert.True((result & WindowManager.WS_EX_APPWINDOW) != 0,
            "WS_EX_APPWINDOW should be set");
    }

    [Fact]
    public void ComputeVisibleStyle_IsIdempotent()
    {
        uint style = WindowManager.WS_EX_TOOLWINDOW;
        uint once = WindowManager.ComputeVisibleStyle(style);
        uint twice = WindowManager.ComputeVisibleStyle(once);

        Assert.Equal(once, twice);
    }

    // ─── IsStyleExcluded ────────────────────────────────────────────────

    [Fact]
    public void IsStyleExcluded_ReturnsTrueWhenToolWindowBitSet()
    {
        uint style = WindowManager.WS_EX_TOOLWINDOW;
        Assert.True(WindowManager.IsStyleExcluded(style));
    }

    [Fact]
    public void IsStyleExcluded_ReturnsFalseWhenToolWindowBitClear()
    {
        uint style = WindowManager.WS_EX_APPWINDOW;
        Assert.False(WindowManager.IsStyleExcluded(style));
    }

    [Fact]
    public void IsStyleExcluded_ReturnsFalseForZeroStyle()
    {
        Assert.False(WindowManager.IsStyleExcluded(0));
    }

    [Fact]
    public void IsStyleExcluded_ReturnsTrueWhenToolWindowBitSetAmongOthers()
    {
        uint style = WindowManager.WS_EX_TOOLWINDOW | 0x00080000 | 0x00000008;
        Assert.True(WindowManager.IsStyleExcluded(style));
    }

    // ─── Round-trip: excluded → visible → excluded ──────────────────────

    [Fact]
    public void RoundTrip_ExcludeThenVisible_PreservesOtherBits()
    {
        uint original = 0x00080000 | 0x00000008; // LAYERED | TOPMOST

        uint excluded = WindowManager.ComputeExcludedStyle(original);
        uint visible = WindowManager.ComputeVisibleStyle(excluded);

        // The TOOLWINDOW and APPWINDOW bits are toggled, but other bits survive.
        Assert.True((visible & 0x00080000) != 0, "LAYERED should survive round-trip");
        Assert.True((visible & 0x00000008) != 0, "TOPMOST should survive round-trip");
        Assert.True((visible & WindowManager.WS_EX_TOOLWINDOW) == 0,
            "TOOLWINDOW should be cleared after round-trip");
    }

    [Fact]
    public void RoundTrip_ExcludeThenVisibleThenExclude_RestoresExcludedState()
    {
        uint original = 0;
        uint excluded1 = WindowManager.ComputeExcludedStyle(original);
        uint visible = WindowManager.ComputeVisibleStyle(excluded1);
        uint excluded2 = WindowManager.ComputeExcludedStyle(visible);

        Assert.True(WindowManager.IsStyleExcluded(excluded2));
        Assert.False(WindowManager.IsStyleExcluded(visible));
    }

    // ─── Constants ──────────────────────────────────────────────────────

    [Fact]
    public void WS_EX_TOOLWINDOW_HasCorrectValue()
    {
        Assert.Equal(0x00000080u, WindowManager.WS_EX_TOOLWINDOW);
    }

    [Fact]
    public void WS_EX_APPWINDOW_HasCorrectValue()
    {
        Assert.Equal(0x00040000u, WindowManager.WS_EX_APPWINDOW);
    }
}
