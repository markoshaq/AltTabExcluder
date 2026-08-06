using System;
using System.Linq;
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
    private static TrayIconManager? _tray;
    private static HotkeyManager? _hotkey;
    private static RuleEngine? _rules;
    private static WindowEventWatcher? _watcher;
    private static AppSettings? _settings;

    [STAThread]
    private static void Main()
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
        _tray.RestartRequested += (_, _) => ShutdownApp();
        _tray.ExitRequested += (_, _) => ShutdownApp();
        _tray.SetStartupChecked(StartupManager.IsEnabled);
        _tray.SetHotkeyLabel(_settings.HotkeyLabel);

        // Global hotkey: configurable, defaults to Win+Alt+X.
        _hotkey = new HotkeyManager();
        _hotkey.SetHotkey(_settings.HotkeyModifiers, _settings.HotkeyKey);
        _hotkey.HotkeyPressed += (_, _) => OnHotkeyPressed();
        if (_hotkey.Register())
            _tray.SetHotkeyChecked(true);
        else
            _tray.ShowNotification("AltTabExcluder",
                $"Could not register {_settings.HotkeyLabel} — another app may own it. You can still use Quick Exclude.",
                Forms.ToolTipIcon.Warning);

        // Auto-apply rules to newly created windows.
        _watcher = new WindowEventWatcher(_rules);
        _watcher.Install();

        Application.Run();
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
        }
        else
        {
            _hotkey.Unregister();
            _tray?.SetHotkeyChecked(false);
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
            _hotkey.Register();
            _tray?.ShowNotification("AltTabExcluder",
                $"Could not register {AppSettings.FormatHotkey(dialog.Modifiers, dialog.Key)} — it may be in use by another app.",
                Forms.ToolTipIcon.Warning);
            return;
        }

        // Persist the new hotkey.
        _settings.HotkeyModifiers = dialog.Modifiers | 0x4000; // add NoRepeat
        _settings.HotkeyKey = dialog.Key;
        _settings.Save();

        string label = _settings.HotkeyLabel;
        _tray?.SetHotkeyLabel(label);
        _tray?.SetHotkeyChecked(true);
        _tray?.ShowNotification("AltTabExcluder",
            $"Hotkey changed to {label}.",
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
