using System.Diagnostics;
using AltTabExcluder.Services;

namespace AltTabExcluder.Tests.Integration;

/// <summary>
/// Integration tests for <see cref="WindowEnumerationService"/> — verifies
/// enumeration and filtering on a live desktop session. Note: test windows
/// created in the test process are filtered out by design (the service
/// excludes the current process), so these tests focus on filtering
/// correctness and result integrity. Tagged "Integration" so they can be
/// filtered out of headless runs.
/// </summary>
[Trait("Category", "Integration")]
public class WindowEnumerationServiceIntegrationTests : IDisposable
{
    private readonly Form _form;

    public WindowEnumerationServiceIntegrationTests()
    {
        // Create a form to verify it is NOT in the enumeration (own-process filter).
        _form = IntegrationTestHelper.CreateTestForm("WES_Filter_Test_" + Guid.NewGuid().ToString("N")[..8]);
    }

    public void Dispose()
    {
        _form?.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void Enumerate_ReturnsNonEmptyList_OnDesktopSession()
    {
        var windows = WindowEnumerationService.GetOpenWindows();

        // On a live desktop session there should be at least some windows
        // (explorer, other apps, etc.). If this fails, the test is likely
        // running headless.
        Assert.NotEmpty(windows);
    }

    [Fact]
    public void Enumerate_ExcludesShellWindows()
    {
        var windows = WindowEnumerationService.GetOpenWindows();

        // All enumerated windows should have a non-empty title (shell windows
        // like the desktop and taskbar are filtered by class name).
        Assert.DoesNotContain(windows, w => string.IsNullOrWhiteSpace(w.WindowTitle));
    }

    [Fact]
    public void Enumerate_ExcludesAppOwnWindows()
    {
        // The test process's windows should NOT appear because
        // WindowEnumerationService filters out the current process.
        var windows = WindowEnumerationService.GetOpenWindows();
        int currentPid = Environment.ProcessId;

        Assert.DoesNotContain(windows, w => w.ProcessId == currentPid);
    }

    [Fact]
    public void Enumerate_AllWindowsHaveValidMetadata()
    {
        var windows = WindowEnumerationService.GetOpenWindows();

        foreach (var w in windows)
        {
            Assert.True(w.Hwnd != IntPtr.Zero, "All windows should have a valid HWND");
            Assert.True(w.ProcessId > 0, "All windows should have a valid PID");
            Assert.False(string.IsNullOrWhiteSpace(w.ProcessName), "All windows should have a process name");
            Assert.False(string.IsNullOrWhiteSpace(w.WindowTitle), "All windows should have a title");
        }
    }

    [Fact]
    public void Enumerate_ResultsAreSortedByProcessNameThenTitle()
    {
        var windows = WindowEnumerationService.GetOpenWindows();

        for (int i = 1; i < windows.Count; i++)
        {
            int cmp = string.Compare(windows[i - 1].ProcessName, windows[i].ProcessName,
                StringComparison.OrdinalIgnoreCase);
            if (cmp == 0)
            {
                cmp = string.Compare(windows[i - 1].WindowTitle, windows[i].WindowTitle,
                    StringComparison.OrdinalIgnoreCase);
            }
            Assert.True(cmp <= 0, "Windows should be sorted by process name then title");
        }
    }
}
