using System.Windows.Forms;
using AltTabExcluder.Services;
using AltTabExcluder.UI;
using Windows.Win32;
using Windows.Win32.Foundation;
using Forms = System.Windows.Forms;

namespace AltTabExcluder.Tray;

/// <summary>
/// Coordinates event handling for the tray menu and global hotkey. Extracted
/// from <see cref="Program"/> to keep the entry point focused on wiring and
/// lifecycle, while this class handles the "what happens when the user does X"
/// logic: hotkey toggle, hotkey change, restore-all, startup toggle, about,
/// restart-as-admin, and the global hotkey press itself.
/// </summary>
internal sealed class TrayEventCoordinator
{
    private readonly TrayIconManager _tray;
    private readonly HotkeyManager _hotkey;
    private readonly RuleEngine _rules;
    private readonly WindowEventWatcher _watcher;
    private readonly AppSettings _settings;
    private readonly ExclusionService _exclusion;

    /// <summary>
    /// Called by <see cref="OnRestartRequested"/> after the elevated process
    /// has been launched and services disposed. The caller (<see cref="Program"/>)
    /// uses this to release the single-instance mutex and exit the message loop.
    /// </summary>
    private readonly Action _onRestartTeardown;

    /// <summary>
    /// Called by <see cref="ShutdownApp"/> after services are disposed. The
    /// caller uses this to exit the message loop.
    /// </summary>
    private readonly Action _onExit;

    internal TrayEventCoordinator(
        TrayIconManager tray,
        HotkeyManager hotkey,
        RuleEngine rules,
        WindowEventWatcher watcher,
        AppSettings settings,
        ExclusionService exclusion,
        Action onRestartTeardown,
        Action onExit)
    {
        _tray = tray;
        _hotkey = hotkey;
        _rules = rules;
        _watcher = watcher;
        _settings = settings;
        _exclusion = exclusion;
        _onRestartTeardown = onRestartTeardown;
        _onExit = onExit;
    }

    /// <summary>
    /// Sweeps currently open windows and applies any matching persistent rules.
    /// Called once at startup to cover windows that existed before the
    /// <see cref="WindowEventWatcher"/> hook was installed. Elevated target
    /// windows are skipped (with a one-time notification) when AltTabExcluder
    /// itself is not elevated, since UIPI blocks the style change.
    /// </summary>
    internal void ApplyRulesToOpenWindows()
    {
        bool weAreElevated = ElevationDetector.IsCurrentProcessElevated();
        int skipped = 0;

        foreach (var w in WindowEnumerationService.GetOpenWindows(_exclusion))
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
                AppLogger.LogWarning(ex, $"Startup rule apply failed for '{w.ProcessName}'");
            }
        }

        if (skipped > 0)
        {
            _tray.ShowNotification("AltTabExcluder — elevation required",
                $"{skipped} elevated window(s) could not be excluded. Restart AltTabExcluder as Administrator to manage them.",
                Forms.ToolTipIcon.Warning);
        }
    }

    /// <summary>Handles the global hotkey: toggle the focused window.</summary>
    internal void OnHotkeyPressed()
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
                _tray.ShowNotification("AltTabExcluder — elevation required",
                    "The focused window is running as Administrator. Restart AltTabExcluder as Administrator (tray menu) to toggle it.",
                    Forms.ToolTipIcon.Warning);
                return;
            }

            _exclusion.Toggle(hwnd);
        }
        catch (Exception ex)
        {
            AppLogger.LogWarning(ex, "OnHotkeyPressed failed");
        }
    }

    internal void OnHotkeyToggleRequested(bool enabled)
    {
        if (enabled)
        {
            bool ok = _hotkey.Register();
            _tray.SetHotkeyChecked(ok);
            if (!ok)
                _tray.ShowNotification("AltTabExcluder",
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
            _tray.SetHotkeyChecked(false);
            _settings.HotkeyEnabled = false;
            _settings.Save();
        }
    }

    internal void OnChangeHotkeyRequested()
    {
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
                _tray.SetHotkeyChecked(true);
                _tray.ShowNotification("AltTabExcluder",
                    $"Could not register {AppSettings.FormatHotkey(dialog.Modifiers, dialog.Key)} — it may be in use by another app.",
                    Forms.ToolTipIcon.Warning);
            }
            else
            {
                // The old combo is also no longer available — the hotkey is now
                // disabled. Surface this so the user knows to pick a new combo.
                _tray.SetHotkeyChecked(false);
                if (_settings.HotkeyEnabled)
                {
                    _settings.HotkeyEnabled = false;
                    _settings.Save();
                }
                _tray.ShowNotification("AltTabExcluder",
                    $"Could not register {AppSettings.FormatHotkey(dialog.Modifiers, dialog.Key)}, and the previous hotkey is also no longer available. Please choose a different combination.",
                    Forms.ToolTipIcon.Warning);
            }
            return;
        }

        // Persist the new hotkey.
        _settings.HotkeyModifiers = dialog.Modifiers | Win32Constants.MOD_NOREPEAT; // add NoRepeat
        _settings.HotkeyKey = dialog.Key;
        _settings.HotkeyEnabled = true;
        _settings.Save();

        string label = _settings.HotkeyLabel;
        _tray.SetHotkeyLabel(label);
        _tray.SetTrayTooltip(label);
        _tray.SetHotkeyChecked(true);
        _tray.ShowNotification("AltTabExcluder",
            $"Hotkey changed to {label}.",
            Forms.ToolTipIcon.Info);
    }

    internal void OnAboutRequested()
    {
        using var dialog = new AboutDialog(_settings.HotkeyLabel);
        dialog.ShowDialog();
    }

    internal void OnRestoreAllRequested()
    {
        // Un-exclude every window that AltTabExcluder excluded. Natural tool
        // windows (not excluded by us) are left untouched.
        int count = _exclusion.RestoreAll();

        _tray.ShowNotification("AltTabExcluder",
            count > 0 ? $"Restored {count} window(s) to Alt+Tab." : "No windows to restore.",
            Forms.ToolTipIcon.Info);
    }

    internal void OnStartupToggleRequested(bool enabled)
    {
        try
        {
            StartupManager.SetEnabled(enabled);
            _tray.SetStartupChecked(StartupManager.IsEnabled);
        }
        catch (Exception ex)
        {
            _tray.SetStartupChecked(StartupManager.IsEnabled);
            _tray.ShowNotification("AltTabExcluder",
                $"Could not change the startup setting: {ex.Message}",
                Forms.ToolTipIcon.Warning);
            AppLogger.LogWarning(ex, "Failed to change startup setting");
        }
    }

    /// <summary>
    /// Restarts the app as Administrator. The elevated process is started
    /// FIRST — if the user declines the UAC prompt, the current instance
    /// stays alive with no state changed. Only after the new process is
    /// successfully launched do we persist state, dispose services, and
    /// invoke <see cref="_onRestartTeardown"/> (which releases the
    /// single-instance mutex and exits).
    /// </summary>
    internal void OnRestartRequested()
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
        PersistExcludedByUs();

        // 3. Dispose services (unregister hotkey, unhook events, hide tray icon).
        _watcher.Dispose();
        _hotkey.Dispose();
        _tray.Dispose();

        // 4. Release the single-instance mutex and exit — handled by Program
        //    via the _onRestartTeardown callback.
        _onRestartTeardown();
    }

    internal void ShutdownApp()
    {
        // Persist the excluded-by-us set so Quick Exclude works after restart.
        PersistExcludedByUs();
        _watcher.Dispose();
        _hotkey.Dispose();
        _tray.Dispose();
        _onExit();
    }

    /// <summary>Saves the current excluded-by-us HWND set to settings.</summary>
    private void PersistExcludedByUs()
    {
        _settings.ExcludedByUs = _exclusion.Tracker.GetForSave().ToList();
        _settings.Save();
    }
}
