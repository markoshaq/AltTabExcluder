using System.Diagnostics;
using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Accessibility;

namespace AltTabExcluder.Services;

/// <summary>
/// Watches for new top-level windows via <c>SetWinEventHook</c> for
/// <c>EVENT_OBJECT_CREATE</c> and <c>EVENT_OBJECT_SHOW</c>, and auto-applies
/// any matching <see cref="RuleEngine"/> rule to them.
///
/// <para>
/// <b>Threading:</b> the hook is installed with <c>WINEVENT_OUTOFCONTEXT</c> and
/// <c>WINEVENT_SKIPOWNPROCESS</c>, so callbacks arrive on the installing thread
/// via its message loop (the WinForms UI thread). No DLL injection occurs. Events
/// can be slightly delayed and may fire for non-window objects, so each callback
/// is validated (<c>IsWindow</c>, <c>IsWindowVisible</c>, has a title, is
/// top-level) before a rule is applied.
/// </para>
/// <para>
/// <c>EVENT_OBJECT_CREATE</c> fires when a window is created, but the window may
/// not yet be visible or have its title set. <c>EVENT_OBJECT_SHOW</c> fires when
/// a window becomes visible — this catches slow-starting apps (Electron, Java,
/// UWP) that show their window well after creation. A short debounce timer is
/// also used as a fallback for windows that aren't yet ready when either event
/// fires.
/// </para>
/// </summary>
public sealed class WindowEventWatcher : IDisposable
{
    private const uint EVENT_OBJECT_CREATE = 0x8000;
    private const uint EVENT_OBJECT_DESTROY = 0x8001;
    private const uint EVENT_OBJECT_SHOW = 0x8002;
    private const uint WINEVENT_OUTOFCONTEXT = 0x0000;
    private const uint WINEVENT_SKIPOWNPROCESS = 0x0002;
    private const int OBJID_WINDOW = 0x00000000;
    private const int CHILDID_SELF = 0;
    private const uint GA_ROOT = 2; // GetAncestor flags: retrieve the root window

    // GetAncestor is not in CsWin32's metadata set, so we declare it as a raw
    // P/Invoke (like NativeElevationInterop / HotkeyPickerDialog do for their
    // non-CsWin32 APIs).
    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern IntPtr GetAncestor(IntPtr hwnd, uint gaFlags);

    private readonly RuleEngine _rules;
    private HWINEVENTHOOK _hook;
    private WINEVENTPROC? _proc; // keep delegate alive to avoid GC while hooked
    private bool _disposed;

    // Tracks HWNDs we've already applied a rule to (or confirmed no rule
    // matches). Prevents EVENT_OBJECT_SHOW from re-applying to a window that
    // was already handled on EVENT_OBJECT_CREATE. Pruned when stale.
    private readonly HashSet<IntPtr> _processed = new();

    public WindowEventWatcher(RuleEngine rules)
    {
        _rules = rules;
    }

    /// <summary>Installs the hook on the calling (UI) thread.</summary>
    public void Install()
    {
        if (_hook != default)
            return;

        _proc = OnEvent;
        // Hook the event range [EVENT_OBJECT_CREATE, EVENT_OBJECT_SHOW] to
        // catch both creation and visibility changes. EVENT_OBJECT_SHOW covers
        // slow-starting apps that don't show their window until well after
        // creation (Electron, Java, UWP).
        _hook = PInvoke.SetWinEventHook(
            EVENT_OBJECT_CREATE, EVENT_OBJECT_SHOW,
            default(HMODULE), _proc,
            0, 0, // all processes/threads (skipping our own via flag)
            WINEVENT_OUTOFCONTEXT | WINEVENT_SKIPOWNPROCESS);
    }

    private void OnEvent(HWINEVENTHOOK hWinEventHook, uint @event, HWND hwnd,
        int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
    {
        // Handle DESTROY: prune the HWND from _processed so recycled handles
        // aren't skipped on the next CREATE/SHOW. Removing a non-existent entry
        // is a no-op, so this is always safe.
        if (@event == EVENT_OBJECT_DESTROY)
        {
            if (idObject == OBJID_WINDOW && idChild == CHILDID_SELF && hwnd != default)
                _processed.Remove((IntPtr)hwnd);
            return;
        }

        // Only handle CREATE and SHOW events.
        if (@event != EVENT_OBJECT_CREATE && @event != EVENT_OBJECT_SHOW)
            return;

        // Only top-level window objects, not child/sub-objects.
        if (idObject != OBJID_WINDOW || idChild != CHILDID_SELF)
            return;
        if (hwnd == default)
            return;

        // EVENT_OBJECT_CREATE/SHOW fire for child windows too; only apply rules
        // to top-level windows (GetAncestor(GA_ROOT) == self). Applying
        // WS_EX_TOOLWINDOW to child windows is wasteful and can have unintended
        // visual effects on the target app.
        if (GetAncestor((IntPtr)hwnd, GA_ROOT) != (IntPtr)hwnd)
            return;

        // Skip windows we've already processed — EVENT_OBJECT_SHOW may fire
        // after EVENT_OBJECT_CREATE for the same window. The check is done
        // inside TryApply (not here) so that a deferred CREATE re-check or a
        // later SHOW event can still pick up a window that wasn't ready.
        TryApply(hwnd, immediate: true);
    }

    /// <summary>
    /// Resolves the process name for <paramref name="hwnd"/> and applies any
    /// matching rule. When <paramref name="immediate"/> is false, the check is
    /// deferred briefly to let the window finish initializing. The HWND is
    /// marked as processed only once we actually attempt the apply (not when
    /// deferring), so a later EVENT_OBJECT_SHOW can still catch a window that
    /// wasn't ready on CREATE.
    /// </summary>
    private void TryApply(HWND hwnd, bool immediate)
    {
        if (_disposed)
            return;

        if (!PInvoke.IsWindow(hwnd) || !PInvoke.IsWindowVisible(hwnd))
        {
            if (immediate)
            {
                // Window may become visible shortly; re-check once after a
                // short delay using a one-shot WinForms timer. Don't mark as
                // processed yet — EVENT_OBJECT_SHOW may fire first and handle it.
                var timer = new System.Windows.Forms.Timer { Interval = 300 };
                timer.Tick += (_, _) =>
                {
                    timer.Stop();
                    timer.Dispose();
                    // Guard against firing after the watcher has been disposed.
                    if (!_disposed)
                        TryApply(hwnd, immediate: false);
                };
                timer.Start();
            }
            return;
        }

        // Window is visible — mark as processed so we don't re-apply on a
        // later EVENT_OBJECT_SHOW for the same HWND.
        if (!_processed.Add((IntPtr)hwnd))
            return;

        string? procName = TryGetProcessName(hwnd);
        if (string.IsNullOrEmpty(procName))
            return;

        // Skip elevated target windows when we're not elevated — UIPI blocks
        // the style change and ApplyTo would silently fail. No notification
        // here (this fires per-window-creation); the startup sweep warns once.
        if (!ElevationDetector.IsCurrentProcessElevated() &&
            ElevationDetector.IsWindowElevated((IntPtr)hwnd))
            return;

        try
        {
            _rules.ApplyTo((IntPtr)hwnd, procName);
        }
        catch (Exception ex)
        {
            AppLogger.LogWarning(ex, $"WindowEventWatcher apply failed for '{procName}'");
        }
    }

    private static string? TryGetProcessName(HWND hwnd)
    {
        try
        {
            uint pid;
            unsafe { PInvoke.GetWindowThreadProcessId(hwnd, &pid); }
            if (pid == 0 || pid == Environment.ProcessId)
                return null;

            using var proc = Process.GetProcessById((int)pid);
            return proc.ProcessName;
        }
        catch (Exception ex)
        {
            AppLogger.LogDebug($"TryGetProcessName failed for HWND {hwnd}: {ex.Message}");
            return null;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        if (_hook != default)
        {
            try { PInvoke.UnhookWinEvent(_hook); }
            catch (Exception ex) { AppLogger.LogWarning(ex, "UnhookWinEvent failed during dispose"); }
            _hook = default;
        }
        _proc = null;
        _disposed = true;
    }
}
