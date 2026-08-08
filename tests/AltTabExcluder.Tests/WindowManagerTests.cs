using AltTabExcluder;

namespace AltTabExcluder.Tests;

/// <summary>
/// Tests for the pure style-bit math in <see cref="WindowStyleMath"/>.
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
        uint result = WindowStyleMath.ComputeExcludedStyle(style);

        Assert.True((result & WindowStyleMath.WS_EX_TOOLWINDOW) != 0,
            "WS_EX_TOOLWINDOW should be set");
        Assert.True((result & WindowStyleMath.WS_EX_APPWINDOW) == 0,
            "WS_EX_APPWINDOW should be cleared");
    }

    [Fact]
    public void ComputeExcludedStyle_PreservesOtherBits()
    {
        // WS_EX_LAYERED = 0x00080000, WS_EX_TOPMOST = 0x00000008
        uint otherBits = 0x00080000 | 0x00000008;
        uint result = WindowStyleMath.ComputeExcludedStyle(otherBits);

        Assert.True((result & 0x00080000) != 0, "WS_EX_LAYERED should be preserved");
        Assert.True((result & 0x00000008) != 0, "WS_EX_TOPMOST should be preserved");
        Assert.True((result & WindowStyleMath.WS_EX_TOOLWINDOW) != 0,
            "WS_EX_TOOLWINDOW should be set");
    }

    [Fact]
    public void ComputeExcludedStyle_StripsAppWindowEvenIfAlreadySet()
    {
        uint style = WindowStyleMath.WS_EX_APPWINDOW;
        uint result = WindowStyleMath.ComputeExcludedStyle(style);

        Assert.True((result & WindowStyleMath.WS_EX_APPWINDOW) == 0,
            "WS_EX_APPWINDOW should be cleared even if it was set");
        Assert.True((result & WindowStyleMath.WS_EX_TOOLWINDOW) != 0,
            "WS_EX_TOOLWINDOW should be set");
    }

    [Fact]
    public void ComputeExcludedStyle_IsIdempotent()
    {
        uint style = 0;
        uint once = WindowStyleMath.ComputeExcludedStyle(style);
        uint twice = WindowStyleMath.ComputeExcludedStyle(once);

        Assert.Equal(once, twice);
    }

    // ─── ComputeVisibleStyle ────────────────────────────────────────────

    [Fact]
    public void ComputeVisibleStyle_StripsToolWindow_AddsAppWindow()
    {
        uint style = WindowStyleMath.WS_EX_TOOLWINDOW;
        uint result = WindowStyleMath.ComputeVisibleStyle(style);

        Assert.True((result & WindowStyleMath.WS_EX_TOOLWINDOW) == 0,
            "WS_EX_TOOLWINDOW should be cleared");
        Assert.True((result & WindowStyleMath.WS_EX_APPWINDOW) != 0,
            "WS_EX_APPWINDOW should be set");
    }

    [Fact]
    public void ComputeVisibleStyle_PreservesOtherBits()
    {
        uint otherBits = 0x00080000 | WindowStyleMath.WS_EX_TOOLWINDOW;
        uint result = WindowStyleMath.ComputeVisibleStyle(otherBits);

        Assert.True((result & 0x00080000) != 0, "WS_EX_LAYERED should be preserved");
        Assert.True((result & WindowStyleMath.WS_EX_TOOLWINDOW) == 0,
            "WS_EX_TOOLWINDOW should be cleared");
        Assert.True((result & WindowStyleMath.WS_EX_APPWINDOW) != 0,
            "WS_EX_APPWINDOW should be set");
    }

    [Fact]
    public void ComputeVisibleStyle_IsIdempotent()
    {
        uint style = WindowStyleMath.WS_EX_TOOLWINDOW;
        uint once = WindowStyleMath.ComputeVisibleStyle(style);
        uint twice = WindowStyleMath.ComputeVisibleStyle(once);

        Assert.Equal(once, twice);
    }

    // ─── IsStyleExcluded ────────────────────────────────────────────────

    [Fact]
    public void IsStyleExcluded_ReturnsTrueWhenToolWindowBitSet()
    {
        uint style = WindowStyleMath.WS_EX_TOOLWINDOW;
        Assert.True(WindowStyleMath.IsStyleExcluded(style));
    }

    [Fact]
    public void IsStyleExcluded_ReturnsFalseWhenToolWindowBitClear()
    {
        uint style = WindowStyleMath.WS_EX_APPWINDOW;
        Assert.False(WindowStyleMath.IsStyleExcluded(style));
    }

    [Fact]
    public void IsStyleExcluded_ReturnsFalseForZeroStyle()
    {
        Assert.False(WindowStyleMath.IsStyleExcluded(0));
    }

    [Fact]
    public void IsStyleExcluded_ReturnsTrueWhenToolWindowBitSetAmongOthers()
    {
        uint style = WindowStyleMath.WS_EX_TOOLWINDOW | 0x00080000 | 0x00000008;
        Assert.True(WindowStyleMath.IsStyleExcluded(style));
    }

    // ─── Round-trip: excluded → visible → excluded ──────────────────────

    [Fact]
    public void RoundTrip_ExcludeThenVisible_PreservesOtherBits()
    {
        uint original = 0x00080000 | 0x00000008; // LAYERED | TOPMOST

        uint excluded = WindowStyleMath.ComputeExcludedStyle(original);
        uint visible = WindowStyleMath.ComputeVisibleStyle(excluded);

        // The TOOLWINDOW and APPWINDOW bits are toggled, but other bits survive.
        Assert.True((visible & 0x00080000) != 0, "LAYERED should survive round-trip");
        Assert.True((visible & 0x00000008) != 0, "TOPMOST should survive round-trip");
        Assert.True((visible & WindowStyleMath.WS_EX_TOOLWINDOW) == 0,
            "TOOLWINDOW should be cleared after round-trip");
    }

    [Fact]
    public void RoundTrip_ExcludeThenVisibleThenExclude_RestoresExcludedState()
    {
        uint original = 0;
        uint excluded1 = WindowStyleMath.ComputeExcludedStyle(original);
        uint visible = WindowStyleMath.ComputeVisibleStyle(excluded1);
        uint excluded2 = WindowStyleMath.ComputeExcludedStyle(visible);

        Assert.True(WindowStyleMath.IsStyleExcluded(excluded2));
        Assert.False(WindowStyleMath.IsStyleExcluded(visible));
    }

    // ─── Constants ──────────────────────────────────────────────────────

    [Fact]
    public void WS_EX_TOOLWINDOW_HasCorrectValue()
    {
        Assert.Equal(0x00000080u, WindowStyleMath.WS_EX_TOOLWINDOW);
    }

    [Fact]
    public void WS_EX_APPWINDOW_HasCorrectValue()
    {
        Assert.Equal(0x00040000u, WindowStyleMath.WS_EX_APPWINDOW);
    }
}
