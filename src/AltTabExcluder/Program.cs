using System.Windows.Forms;
using AltTabExcluder.Services;
using AltTabExcluder.Tray;
using Windows.Win32;
using Forms = System.Windows.Forms;

namespace AltTabExcluder;

/// <summary>
/// Pure WinForms entry point. The app runs as a tray-resident utility with no
/// main window. All user interaction happens through the tray context menu.
/// <see cref="Program"/> handles single-instance enforcement, service wiring,
/// and lifecycle; event handling is delegated to
/// <see cref="TrayEventCoordinator"/>.
/// </summary>
internal static class Program
{
    private const string SingleInstanceMutexName = @"Global\AltTabExcluder_SingleInstance";

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
            var settings = AppSettings.Load();

            // Build the exclusion stack: tracker (state) + service (orchestration).
            var tracker = new ExclusionTracker();
            tracker.Load(settings.ExcludedByUs.Select(e => (e.Hwnd, e.Pid)));
            var exclusion = new ExclusionService(tracker);

            // Prune stale HWNDs on startup — windows from the previous session
            // may have closed, and their HWNDs may have been recycled.
            exclusion.PruneStale();

            // RuleEngine applies rules via the exclusion service callback.
            var rules = new RuleEngine((hwnd, excluded) => exclusion.SetExcluded(hwnd, excluded));

            var tray = new TrayIconManager(rules, exclusion);
            var hotkey = new HotkeyManager();
            var watcher = new WindowEventWatcher(rules);

            // Wire the coordinator — it owns all event handling logic.
            var coordinator = new TrayEventCoordinator(
                tray, hotkey, rules, watcher, settings, exclusion,
                onRestartTeardown: ReleaseSingleInstanceMutexAndExit,
                onExit: () => Application.Exit());

            // Tray events → coordinator
            tray.HotkeyToggleRequested += (_, enabled) => coordinator.OnHotkeyToggleRequested(enabled);
            tray.ChangeHotkeyRequested += (_, _) => coordinator.OnChangeHotkeyRequested();
            tray.StartupToggleRequested += (_, enabled) => coordinator.OnStartupToggleRequested(enabled);
            tray.RestartRequested += (_, _) => coordinator.OnRestartRequested();
            tray.ExitRequested += (_, _) => coordinator.ShutdownApp();
            tray.AboutRequested += (_, _) => coordinator.OnAboutRequested();
            tray.RestoreAllRequested += (_, _) => coordinator.OnRestoreAllRequested();
            tray.SetStartupChecked(StartupManager.IsEnabled);
            tray.SetHotkeyLabel(settings.HotkeyLabel);
            tray.SetTrayTooltip(settings.HotkeyLabel);

            // Global hotkey: configurable, defaults to Win+Alt+X.
            // Respects the persisted enabled/disabled state so the user's
            // choice survives restarts.
            hotkey.SetHotkey(settings.HotkeyModifiers, settings.HotkeyKey);
            hotkey.HotkeyPressed += (_, _) => coordinator.OnHotkeyPressed();
            if (settings.HotkeyEnabled && hotkey.Register())
                tray.SetHotkeyChecked(true);
            else if (settings.HotkeyEnabled)
                tray.ShowNotification("AltTabExcluder",
                    $"Could not register {settings.HotkeyLabel} — another app may own it. You can still use Quick Exclude.",
                    Forms.ToolTipIcon.Warning);

            // Auto-apply rules to newly created windows.
            watcher.Install();

            // Apply existing rules to windows that are already open at startup —
            // the WinEvent hook only covers windows created after this point.
            coordinator.ApplyRulesToOpenWindows();

            Application.Run();
        }
        catch (Exception ex)
        {
            AppLogger.LogException(ex, "Fatal error in Main");
            throw;
        }
        finally
        {
            // Release the single-instance mutex so a future launch can start.
            ReleaseSingleInstanceMutex();
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
    /// Releases and disposes the single-instance mutex. Called from the
    /// <c>finally</c> block in <see cref="Main"/> and from the restart-as-admin
    /// teardown callback.
    /// </summary>
    private static void ReleaseSingleInstanceMutex()
    {
        if (_singleInstanceMutex is not null)
        {
            try { _singleInstanceMutex.ReleaseMutex(); }
            catch (ApplicationException) { /* not owned — ignore */ }
            _singleInstanceMutex.Dispose();
            _singleInstanceMutex = null;
        }
    }

    /// <summary>
    /// Teardown callback for restart-as-admin: release the mutex so the new
    /// elevated instance can acquire it, then exit the message loop.
    /// </summary>
    private static void ReleaseSingleInstanceMutexAndExit()
    {
        ReleaseSingleInstanceMutex();
        Application.Exit();
    }
}
