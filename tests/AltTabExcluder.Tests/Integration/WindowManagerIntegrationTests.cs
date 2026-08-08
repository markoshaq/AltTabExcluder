using AltTabExcluder;

namespace AltTabExcluder.Tests.Integration;

/// <summary>
/// Integration tests for <see cref="WindowManager"/> — exercises the Win32
/// style read/write against real (test-created) windows on a live desktop
/// session. Tagged "Integration" so they can be filtered out of headless runs.
/// </summary>
[Trait("Category", "Integration")]
public class WindowManagerIntegrationTests : IDisposable
{
    private readonly Form _form;

    public WindowManagerIntegrationTests()
    {
        _form = IntegrationTestHelper.CreateTestForm("WM_Integration_Test");
    }

    public void Dispose()
    {
        _form?.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void SetStyle_Exclude_SetsToolWindow_ClearsAppWindow()
    {
        IntPtr hwnd = _form.Handle;

        WindowManager.SetStyle(hwnd, excluded: true);

        uint style = WindowManager.GetExtendedStyle(hwnd);
        Assert.True((style & WindowStyleMath.WS_EX_TOOLWINDOW) != 0,
            "WS_EX_TOOLWINDOW should be set after excluding");
        Assert.True((style & WindowStyleMath.WS_EX_APPWINDOW) == 0,
            "WS_EX_APPWINDOW should be cleared after excluding");
    }

    [Fact]
    public void SetStyle_Restore_ClearsToolWindow_SetsAppWindow()
    {
        IntPtr hwnd = _form.Handle;

        WindowManager.SetStyle(hwnd, excluded: true);
        WindowManager.SetStyle(hwnd, excluded: false);

        uint style = WindowManager.GetExtendedStyle(hwnd);
        Assert.True((style & WindowStyleMath.WS_EX_TOOLWINDOW) == 0,
            "WS_EX_TOOLWINDOW should be cleared after restoring");
        Assert.True((style & WindowStyleMath.WS_EX_APPWINDOW) != 0,
            "WS_EX_APPWINDOW should be set after restoring");
    }

    [Fact]
    public void IsWindowExcluded_ReflectsCurrentStyle()
    {
        IntPtr hwnd = _form.Handle;

        Assert.False(WindowManager.IsWindowExcluded(hwnd));

        WindowManager.SetStyle(hwnd, excluded: true);
        Assert.True(WindowManager.IsWindowExcluded(hwnd));

        WindowManager.SetStyle(hwnd, excluded: false);
        Assert.False(WindowManager.IsWindowExcluded(hwnd));
    }

    [Fact]
    public void ToggleStyle_RoundTrips()
    {
        IntPtr hwnd = _form.Handle;

        bool firstToggle = WindowManager.ToggleStyle(hwnd);
        Assert.True(firstToggle, "First toggle should exclude the window");
        Assert.True(WindowManager.IsWindowExcluded(hwnd));

        bool secondToggle = WindowManager.ToggleStyle(hwnd);
        Assert.False(secondToggle, "Second toggle should restore the window");
        Assert.False(WindowManager.IsWindowExcluded(hwnd));
    }

    [Fact]
    public void SetStyle_StyleChangeIsImmediatelyReadable()
    {
        // Verifies that SetWindowPos(SWP_FRAMECHANGED) makes the style change
        // immediately visible — no delay or deferred update.
        IntPtr hwnd = _form.Handle;

        WindowManager.SetStyle(hwnd, excluded: true);
        uint styleAfterExclude = WindowManager.GetExtendedStyle(hwnd);
        Assert.True((styleAfterExclude & WindowStyleMath.WS_EX_TOOLWINDOW) != 0,
            "Style should be immediately readable as changed");

        WindowManager.SetStyle(hwnd, excluded: false);
        uint styleAfterRestore = WindowManager.GetExtendedStyle(hwnd);
        Assert.True((styleAfterRestore & WindowStyleMath.WS_EX_TOOLWINDOW) == 0,
            "Style should be immediately readable as restored");
    }

    [Fact]
    public void SetStyle_Idempotent_WhenAlreadyInTargetState()
    {
        IntPtr hwnd = _form.Handle;

        WindowManager.SetStyle(hwnd, excluded: true);
        uint style1 = WindowManager.GetExtendedStyle(hwnd);

        // Calling again should be a no-op (SetStyle checks newStyle != ex).
        WindowManager.SetStyle(hwnd, excluded: true);
        uint style2 = WindowManager.GetExtendedStyle(hwnd);

        Assert.Equal(style1, style2);
    }
}
