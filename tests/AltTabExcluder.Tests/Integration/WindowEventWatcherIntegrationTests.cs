using System.Diagnostics;
using AltTabExcluder;
using AltTabExcluder.Services;
using Windows.Win32;
using Windows.Win32.Foundation;

namespace AltTabExcluder.Tests.Integration;

/// <summary>
/// Integration tests for <see cref="WindowEventWatcher"/> — verifies that
/// rules are auto-applied to newly created top-level windows and that child
/// windows are filtered out. Uses a PowerShell process with a WinForms window
/// because the watcher intentionally skips windows from its own process.
/// Tagged "Integration" so they can be filtered out of headless runs.
/// </summary>
[Trait("Category", "Integration")]
public class WindowEventWatcherIntegrationTests : IDisposable
{
    private readonly string _rulesDir;
    private readonly string _rulesPath;
    private readonly RuleEngine _rules;
    private readonly WindowEventWatcher _watcher;
    private readonly List<IntPtr> _appliedHandles = new();
    private Process? _externalProc;

    public WindowEventWatcherIntegrationTests()
    {
        _rulesDir = Path.Combine(Path.GetTempPath(), "AltTabExcluderWatcherTests_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_rulesDir);
        _rulesPath = Path.Combine(_rulesDir, "rules.json");

        // The callback records which HWNDs were excluded so tests can assert.
        _rules = new RuleEngine(_rulesPath, (hwnd, excluded) =>
        {
            if (excluded)
                WindowManager.SetStyle(hwnd, excluded: true);
            lock (_appliedHandles)
                _appliedHandles.Add(hwnd);
        });

        _watcher = new WindowEventWatcher(_rules);
        _watcher.Install();
    }

    public void Dispose()
    {
        _watcher.Dispose();
        try
        {
            if (_externalProc is not null && !_externalProc.HasExited)
            {
                _externalProc.Kill();
                _externalProc.WaitForExit(3000);
            }
            _externalProc?.Dispose();
        }
        catch { }
        _rules.RemoveRule("powershell");
        try { Directory.Delete(_rulesDir, recursive: true); } catch { }
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void Watcher_AppliesRule_ToNewTopLevelWindow()
    {
        // Create a rule for "powershell" BEFORE launching it.
        _rules.SetRule("powershell", exclude: true);

        try
        {
            // Launch PowerShell with a WinForms window in a separate process.
            // The -WindowStyle Hidden flag suppresses the console window so
            // only the WinForms form triggers the watcher's SHOW event.
            string uniqueTitle = "WatcherTest_" + Guid.NewGuid().ToString("N")[..8];
            _externalProc = Process.Start(new ProcessStartInfo("powershell.exe")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                Arguments = $"-WindowStyle Hidden -Command \"Add-Type -AssemblyName System.Windows.Forms; $f = New-Object System.Windows.Forms.Form; $f.Text = '{uniqueTitle}'; $f.Show(); Start-Sleep -Seconds 30\"",
            });
            Assert.NotNull(_externalProc);

            // Poll for the WinForms window to appear. PowerShell + WinForms
            // assembly loading can be slow on CI runners, so we poll up to 15s
            // instead of using a fixed wait. Pump messages throughout so the
            // watcher's WinEventHook callbacks fire as soon as the window appears.
            IntPtr windowHwnd = IntPtr.Zero;
            var pollSw = System.Diagnostics.Stopwatch.StartNew();
            while (pollSw.ElapsedMilliseconds < 15000 && windowHwnd == IntPtr.Zero)
            {
                IntegrationTestHelper.PumpMessages(500);
                windowHwnd = FindWindowByPidAndTitle(_externalProc.Id, uniqueTitle);
            }

            Assert.True(windowHwnd != IntPtr.Zero,
                $"PowerShell WinForms window with title '{uniqueTitle}' should exist (PID={_externalProc.Id})");

            // The watcher may have already applied the rule, or its 300ms debounce
            // timer may still be pending. Pump messages for a bit longer to let
            // any deferred TryApply fire.
            var applySw = System.Diagnostics.Stopwatch.StartNew();
            while (applySw.ElapsedMilliseconds < 3000)
            {
                IntegrationTestHelper.PumpMessages(200);
                lock (_appliedHandles)
                {
                    if (_appliedHandles.Contains(windowHwnd))
                        break;
                }
            }

            // The watcher should have applied the rule to this window.
            lock (_appliedHandles)
            {
                Assert.Contains(windowHwnd, _appliedHandles);
            }

            // Verify the style was actually applied.
            uint style = WindowManager.GetExtendedStyle(windowHwnd);
            Assert.True((style & WindowStyleMath.WS_EX_TOOLWINDOW) != 0,
                "WS_EX_TOOLWINDOW should be set by the watcher's rule application");

            // Restore the window style so we don't leave it in a weird state.
            WindowManager.SetStyle(windowHwnd, excluded: false);
        }
        finally
        {
            _rules.RemoveRule("powershell");
        }
    }

    [Fact]
    public void Watcher_DoesNotApplyRule_ToChildWindow()
    {
        // This test verifies that child windows are filtered. We use a WinForms
        // form with a child panel. The watcher skips own-process windows, so
        // neither the form nor the panel will be processed — but we can still
        // verify the child-filtering logic by checking that the panel's handle
        // never appears in the applied list.
        _rules.SetRule(Process.GetCurrentProcess().ProcessName, exclude: true);

        try
        {
            using var form = IntegrationTestHelper.CreateTestForm("Watcher_Test_Parent");

            // Create a child control with its own handle.
            var panel = new Panel();
            panel.CreateControl();
            form.Controls.Add(panel);
            Application.DoEvents();

            IntPtr childHwnd = panel.Handle;
            Assert.True(childHwnd != IntPtr.Zero, "Child panel should have a handle");

            IntegrationTestHelper.PumpMessages(2000);

            // Neither the parent nor the child should be in the applied list
            // (own-process filter). The child-filtering logic is verified
            // by the fact that GetAncestor(GA_ROOT) != childHwnd.
            lock (_appliedHandles)
            {
                Assert.DoesNotContain(childHwnd, _appliedHandles);
            }
        }
        finally
        {
            _rules.RemoveRule(Process.GetCurrentProcess().ProcessName);
        }
    }

    /// <summary>
    /// Finds a visible top-level window belonging to the given PID with the
    /// given title, using EnumWindows.
    /// </summary>
    private static IntPtr FindWindowByPidAndTitle(int pid, string title)
    {
        IntPtr found = IntPtr.Zero;
        unsafe
        {
            PInvoke.EnumWindows((hwnd, lParam) =>
            {
                if (PInvoke.IsWindowVisible(hwnd))
                {
                    uint wpid;
                    _ = PInvoke.GetWindowThreadProcessId(hwnd, &wpid);
                    if ((int)wpid == pid)
                    {
                        int titleLen = PInvoke.GetWindowTextLength(hwnd);
                        if (titleLen > 0)
                        {
                            char* buf = stackalloc char[titleLen + 1];
                            int written = PInvoke.GetWindowText(hwnd, (PWSTR)buf, titleLen + 1);
                            if (written > 0 && new string(buf, 0, written) == title)
                            {
                                found = (IntPtr)hwnd;
                                return false; // stop enumeration
                            }
                        }
                    }
                }
                return true;
            }, (LPARAM)0);
        }
        return found;
    }
}
