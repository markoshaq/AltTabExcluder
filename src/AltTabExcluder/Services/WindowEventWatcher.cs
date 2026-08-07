using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Accessibility;

namespace AltTabExcluder.Services;

/// <summary>
/// Watches for new top-level windows via <c>SetWinEventHook(EVENT_OBJECT_CREATE)</c>
/// and auto-applies any matching <see cref="RuleEngine"/> rule to them.
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
/// A short debounce is used: a newly created window may not yet be visible or
/// have its title set when <c>EVENT_OBJECT_CREATE</c> fires, so we re-check
/// after a brief delay. This avoids missing windows that finish initializing
/// a moment later.
/// </para>
/// </summary>
public sealed class WindowEventWatcher : IDisposable
{
    private const uint EVENT_OBJECT_CREATE = 0x8000;
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
        _hook = PInvoke.SetWinEventHook(
            EVENT_OBJECT_CREATE, EVENT_OBJECT_CREATE,
            default(HMODULE), _proc,
            0, 0, // all processes/threads (skipping our own via flag)
            WINEVENT_OUTOFCONTEXT | WINEVENT_SKIPOWNPROCESS);
    }

    private void OnEvent(HWINEVENTHOOK hWinEventHook, uint @event, HWND hwnd,
        int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
    {
        // Only top-level window objects, not child/sub-objects.
        if (idObject != OBJID_WINDOW || idChild != CHILDID_SELF)
            return;
        if (hwnd == default)
            return;

        // EVENT_OBJECT_CREATE fires for child windows too; only apply rules to
        // top-level windows (GetAncestor(GA_ROOT) == self). Applying
        // WS_EX_TOOLWINDOW to child windows is wasteful and can have unintended
        // visual effects on the target app.
        if (GetAncestor((IntPtr)hwnd, GA_ROOT) != (IntPtr)hwnd)
            return;

        TryApply(hwnd, immediate: true);
    }

    /// <summary>
    /// Resolves the process name for <paramref name="hwnd"/> and applies any
    /// matching rule. When <paramref name="immediate"/> is false, the check is
    /// deferred briefly to let the window finish initializing.
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
                // short delay using a one-shot WinForms timer.
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

        string? procName = TryGetProcessName(hwnd);
        if (string.IsNullOrEmpty(procName))
            return;

        try
        {
            _rules.ApplyTo((IntPtr)hwnd, procName);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"WindowEventWatcher apply failed: {ex.Message}");
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
        catch
        {
            return null;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        if (_hook != default)
        {
            try { PInvoke.UnhookWinEvent(_hook); }
            catch { /* best effort */ }
            _hook = default;
        }
        _proc = null;
        _disposed = true;
    }
}
