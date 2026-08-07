using System.Windows.Forms;
using AltTabExcluder.Services;
using AltTabExcluder.Tray;
using AltTabExcluder.UI;
using Windows.Win32;
using Windows.Win32.Foundation;
using Forms = System.Windows.Forms;

namespace AltTabExcluder;

/// <summary>
/// Pure WinForms entry point. The app runs as a tray-resident utility with no
/// main window. All user interaction happens through the tray context menu.
/// </summary>
internal static class Program
{
    private const string SingleInstanceMutexName = @"Global\AltTabExcluder_SingleInstance";

    private static TrayIconManager? _tray;
    private static HotkeyManager? _hotkey;
    private static RuleEngine? _rules;
    private static WindowEventWatcher? _watcher;
    private static AppSettings? _settings;

    // Held for the lifetime of the process to prevent a second instance from
    // starting (would otherwise create a duplicate tray icon and fail to
    // register the global hotkey).
    private static Mutex? _singleInstanceMutex;

    [STAThread]
    private static void Main()
    {
        // Ensure only one instance is running — this is a tray-only app, so a
        // second instance would just produce a duplicate icon and a hotkey
        // registration failure. Using a named Mutex (not a Process lookup) is
        // race-free and works across sessions. A short retry loop covers the
        // restart-as-admin flow, where the old instance releases the mutex
        // moments before the new (elevated) instance tries to acquire it.
        _singleInstanceMutex = TryAcquireSingleInstanceMutex();
        if (_singleInstanceMutex is null)
            return;

        try
        {
            ApplicationConfiguration.Initialize();

            // Load persisted settings (hotkey config, excluded-by-us set) and rules.
            _settings = AppSettings.Load();
            WindowManager.LoadExcludedByUs(_settings.ExcludedByUs);
            _rules = new RuleEngine();

            _tray = new TrayIconManager(_rules);
            _tray.HotkeyToggleRequested += (_, enabled) => OnHotkeyToggleRequested(enabled);
            _tray.ChangeHotkeyRequested += (_, _) => OnChangeHotkeyRequested();
            _tray.StartupToggleRequested += (_, enabled) => OnStartupToggleRequested(enabled);
            _tray.RestartRequested += (_, _) => OnRestartRequested();
            _tray.ExitRequested += (_, _) => ShutdownApp();
            _tray.AboutRequested += (_, _) => OnAboutRequested();
            _tray.RestoreAllRequested += (_, _) => OnRestoreAllRequested();
            _tray.SetStartupChecked(StartupManager.IsEnabled);
            _tray.SetHotkeyLabel(_settings.HotkeyLabel);
            _tray.SetTrayTooltip(_settings.HotkeyLabel);

            // Global hotkey: configurable, defaults to Win+Alt+X.
            // Respects the persisted enabled/disabled state so the user's
            // choice survives restarts.
            _hotkey = new HotkeyManager();
            _hotkey.SetHotkey(_settings.HotkeyModifiers, _settings.HotkeyKey);
            _hotkey.HotkeyPressed += (_, _) => OnHotkeyPressed();
            if (_settings.HotkeyEnabled && _hotkey.Register())
                _tray.SetHotkeyChecked(true);
            else if (_settings.HotkeyEnabled)
                _tray.ShowNotification("AltTabExcluder",
                    $"Could not register {_settings.HotkeyLabel} — another app may own it. You can still use Quick Exclude.",
                    Forms.ToolTipIcon.Warning);

            // Auto-apply rules to newly created windows.
            _watcher = new WindowEventWatcher(_rules);
            _watcher.Install();

            // Apply existing rules to windows that are already open at startup —
            // the WinEvent hook only covers windows created after this point.
            ApplyRulesToOpenWindows();

            Application.Run();
        }
        finally
        {
            // Release the single-instance mutex so a future launch can start.
            if (_singleInstanceMutex is not null)
            {
                try { _singleInstanceMutex.ReleaseMutex(); }
                catch (ApplicationException) { /* not owned — ignore */ }
                _singleInstanceMutex.Dispose();
                _singleInstanceMutex = null;
            }
        }
    }

    /// <summary>
    /// Attempts to acquire the single-instance mutex, retrying briefly to
    /// cover the restart-as-admin flow where the old instance releases the
    /// mutex moments before the new (elevated) instance tries to acquire it.
    /// Returns <c>null</c> if the mutex could not be acquired after retrying.
    /// </summary>
    private static Mutex? TryAcquireSingleInstanceMutex()
    {
        for (int i = 0; i < 10; i++)
        {
            var mutex = new Mutex(initiallyOwned: true, name: SingleInstanceMutexName, out bool createdNew);
            if (createdNew)
                return mutex;
            mutex.Dispose();
            Thread.Sleep(200);
        }
        return null;
    }

    /// <summary>
    /// Sweeps currently open windows and applies any matching persistent rules.
    /// Called once at startup to cover windows that existed before the
    /// <see cref="WindowEventWatcher"/> hook was installed. Elevated target
    /// windows are skipped (with a one-time notification) when AltTabExcluder
    /// itself is not elevated, since UIPI blocks the style change.
    /// </summary>
    private static void ApplyRulesToOpenWindows()
    {
        if (_rules is null) return;

        bool weAreElevated = ElevationDetector.IsCurrentProcessElevated();
        int skipped = 0;

        foreach (var w in WindowEnumerationService.GetOpenWindows())
        {
            // Skip elevated targets when we're not elevated — UIPI will block
            // the style change and ApplyTo would silently fail.
            if (!weAreElevated && ElevationDetector.IsWindowElevated(w.Hwnd))
            {
                skipped++;
                continue;
            }

            try { _rules.ApplyTo(w.Hwnd, w.ProcessName); }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Startup rule apply failed for {w.ProcessName}: {ex.Message}");
            }
        }

        if (skipped > 0)
        {
            _tray?.ShowNotification("AltTabExcluder — elevation required",
                $"{skipped} elevated window(s) could not be excluded. Restart AltTabExcluder as Administrator to manage them.",
                Forms.ToolTipIcon.Warning);
        }
    }

    /// <summary>Handles the global hotkey: toggle the focused window.</summary>
    private static void OnHotkeyPressed()
    {
        try
        {
            HWND fg = PInvoke.GetForegroundWindow();
            if (fg == default)
                return;

            IntPtr hwnd = (IntPtr)fg;

            // Edge case: elevated target, non-elevated us → UIPI blocks the style change.
            if (ElevationDetector.IsWindowElevated(hwnd) && !ElevationDetector.IsCurrentProcessElevated())
            {
                _tray?.ShowNotification("AltTabExcluder — elevation required",
                    "The focused window is running as Administrator. Restart AltTabExcluder as Administrator (tray menu) to toggle it.",
                    Forms.ToolTipIcon.Warning);
                return;
            }

            WindowManager.ToggleAltTabVisibility(hwnd);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"OnHotkeyPressed failed: {ex.Message}");
        }
    }

    private static void OnHotkeyToggleRequested(bool enabled)
    {
        if (_hotkey is null || _settings is null) return;
        if (enabled)
        {
            bool ok = _hotkey.Register();
            _tray?.SetHotkeyChecked(ok);
            if (!ok)
                _tray?.ShowNotification("AltTabExcluder",
                    $"Could not register {_settings.HotkeyLabel} — it may be in use by another app.",
                    Forms.ToolTipIcon.Warning);
            else
            {
                _settings.HotkeyEnabled = true;
                _settings.Save();
            }
        }
        else
        {
            _hotkey.Unregister();
            _tray?.SetHotkeyChecked(false);
            _settings.HotkeyEnabled = false;
            _settings.Save();
        }
    }

    private static void OnChangeHotkeyRequested()
    {
        if (_hotkey is null || _settings is null) return;

        using var dialog = new HotkeyPickerDialog(_settings.HotkeyModifiers, _settings.HotkeyKey);
        if (dialog.ShowDialog() != DialogResult.OK || !dialog.HasValidCombo)
            return;

        // Try to register the new combo.
        _hotkey.Unregister();
        _hotkey.SetHotkey(dialog.Modifiers, dialog.Key);

        bool ok = _hotkey.Register();
        if (!ok)
        {
            // Registration failed — revert to the old combo.
            _hotkey.Unregister();
            _hotkey.SetHotkey(_settings.HotkeyModifiers, _settings.HotkeyKey);
            bool reverted = _hotkey.Register();

            if (reverted)
            {
                _tray?.SetHotkeyChecked(true);
                _tray?.ShowNotification("AltTabExcluder",
                    $"Could not register {AppSettings.FormatHotkey(dialog.Modifiers, dialog.Key)} — it may be in use by another app.",
                    Forms.ToolTipIcon.Warning);
            }
            else
            {
                // The old combo is also no longer available — the hotkey is now
                // disabled. Surface this so the user knows to pick a new combo.
                _tray?.SetHotkeyChecked(false);
                if (_settings.HotkeyEnabled)
                {
                    _settings.HotkeyEnabled = false;
                    _settings.Save();
                }
                _tray?.ShowNotification("AltTabExcluder",
                    $"Could not register {AppSettings.FormatHotkey(dialog.Modifiers, dialog.Key)}, and the previous hotkey is also no longer available. Please choose a different combination.",
                    Forms.ToolTipIcon.Warning);
            }
            return;
        }

        // Persist the new hotkey.
        _settings.HotkeyModifiers = dialog.Modifiers | 0x4000; // add NoRepeat
        _settings.HotkeyKey = dialog.Key;
        _settings.HotkeyEnabled = true;
        _settings.Save();

        string label = _settings.HotkeyLabel;
        _tray?.SetHotkeyLabel(label);
        _tray?.SetTrayTooltip(label);
        _tray?.SetHotkeyChecked(true);
        _tray?.ShowNotification("AltTabExcluder",
            $"Hotkey changed to {label}.",
            Forms.ToolTipIcon.Info);
    }

    private static void OnAboutRequested()
    {
        if (_settings is null) return;
        using var dialog = new AboutDialog(_settings.HotkeyLabel);
        dialog.ShowDialog();
    }

    private static void OnRestoreAllRequested()
    {
        // Un-exclude every window that AltTabExcluder excluded. Natural tool
        // windows (not excluded by us) are left untouched.
        WindowManager.PruneStaleHandles();
        var handles = WindowManager.GetExcludedByUsForSave().Select(h => (IntPtr)h).ToList();
        int count = 0;
        foreach (var hwnd in handles)
        {
            try
            {
                WindowManager.SetExcluded(hwnd, false);
                count++;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"RestoreAll failed for {hwnd}: {ex.Message}");
            }
        }

        _tray?.ShowNotification("AltTabExcluder",
            count > 0 ? $"Restored {count} window(s) to Alt+Tab." : "No windows to restore.",
            Forms.ToolTipIcon.Info);
    }

    private static void OnStartupToggleRequested(bool enabled)
    {
        try
        {
            StartupManager.SetEnabled(enabled);
            _tray?.SetStartupChecked(StartupManager.IsEnabled);
        }
        catch (Exception ex)
        {
            _tray?.SetStartupChecked(StartupManager.IsEnabled);
            _tray?.ShowNotification("AltTabExcluder",
                $"Could not change the startup setting: {ex.Message}",
                Forms.ToolTipIcon.Warning);
        }
    }

    /// <summary>
    /// Restarts the app as Administrator. The elevated process is started
    /// FIRST — if the user declines the UAC prompt, the current instance
    /// stays alive with no state changed. Only after the new process is
    /// successfully launched do we persist state, dispose services, and
    /// release the single-instance mutex. The new instance's <c>Main</c>
    /// retries the mutex acquisition briefly (see
    /// <see cref="TryAcquireSingleInstanceMutex"/>) to cover the race window
    /// between the old instance releasing the mutex and the new one acquiring it.
    /// </summary>
    private static void OnRestartRequested()
    {
        string? exe = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exe))
            return;

        // 1. Try to start the elevated process first. If the user declines
        //    the UAC prompt, we stay alive — no state has been changed yet.
        try
        {
            System.Diagnostics.Process.Start(
                new System.Diagnostics.ProcessStartInfo(exe)
                {
                    UseShellExecute = true,
                    Verb = "runas",
                });
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // User declined the UAC prompt — stay alive, do nothing.
            return;
        }

        // 2. New process is starting — persist state so it loads fresh exclusions.
        if (_settings is not null)
        {
            _settings.ExcludedByUs = WindowManager.GetExcludedByUsForSave().ToList();
            _settings.Save();
        }

        // 3. Dispose services (unregister hotkey, unhook events, hide tray icon).
        _watcher?.Dispose();
        _hotkey?.Dispose();
        _tray?.Dispose();

        // 4. Release the single-instance mutex so the new instance can acquire it.
        //    The new instance retries acquisition for up to ~2s (see Main).
        if (_singleInstanceMutex is not null)
        {
            try { _singleInstanceMutex.ReleaseMutex(); }
            catch (ApplicationException) { /* not owned — ignore */ }
            _singleInstanceMutex.Dispose();
            _singleInstanceMutex = null;
        }

        // 5. Exit. The finally block in Main is now a no-op for the mutex
        //    (already released and nulled out above).
        Application.Exit();
    }

    private static void ShutdownApp()
    {
        // Persist the excluded-by-us set so Quick Exclude works after restart.
        if (_settings is not null)
        {
            _settings.ExcludedByUs = WindowManager.GetExcludedByUsForSave().ToList();
            _settings.Save();
        }
        _watcher?.Dispose();
        _hotkey?.Dispose();
        _tray?.Dispose();
        Application.Exit();
    }
}
