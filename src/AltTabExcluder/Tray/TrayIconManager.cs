using System.Windows.Forms;
using AltTabExcluder.Services;

namespace AltTabExcluder.Tray;

/// <summary>
/// Owns the system-tray <see cref="NotifyIcon"/> and its context menu. All user
/// interaction happens here: Quick Exclude (toggle live windows), Always Exclude
/// (persist rules per process), Restore All, Settings (hotkey toggle, change
/// hotkey, startup toggle, restart-as-admin), about, and exit.
/// </summary>
public sealed class TrayIconManager : IDisposable
{
    private readonly NotifyIcon _notifyIcon;
    private readonly ToolStripMenuItem _hotkeyItem;
    private readonly ToolStripMenuItem _quickExcludeItem;
    private readonly ToolStripMenuItem _alwaysExcludeItem;
    private readonly ToolStripMenuItem _startupItem;
    private readonly RuleEngine _rules;
    private readonly ExclusionService _exclusion;

    /// <summary>Flag set when a Quick/Always Exclude item is clicked, so the
    /// parent menu's Closing handler knows to keep the menu open.</summary>
    private bool _excludeClicked;

    public event EventHandler? ExitRequested;

    /// <summary>Raised when the user toggles the global hotkey on/off via tray.</summary>
    public event EventHandler<bool>? HotkeyToggleRequested;

    /// <summary>Raised when the user wants to change the hotkey.</summary>
    public event EventHandler? ChangeHotkeyRequested;

    /// <summary>Raised when the user toggles "Run at Windows startup".</summary>
    public event EventHandler<bool>? StartupToggleRequested;

    /// <summary>Raised when the user wants to restart as Administrator.</summary>
    public event EventHandler? RestartRequested;

    /// <summary>Raised when the user wants to open the About dialog.</summary>
    public event EventHandler? AboutRequested;

    /// <summary>Raised when the user wants to restore all excluded windows.</summary>
    public event EventHandler? RestoreAllRequested;

    public TrayIconManager(RuleEngine rules, ExclusionService exclusion)
    {
        _rules = rules;
        _exclusion = exclusion;

        _notifyIcon = new NotifyIcon
        {
            Icon = LoadTrayIcon(),
            Text = "AltTabExcluder",
            Visible = true,
        };

        var menu = new ContextMenuStrip();

        // Quick Exclude: submenu populated dynamically with all open windows.
        _quickExcludeItem = new ToolStripMenuItem("Quick Exclude")
        {
            ToolTipText = "Toggle Alt+Tab exclusion for any open window.",
        };
        menu.Items.Add(_quickExcludeItem);

        // Always Exclude: submenu of process names with persistent rules.
        _alwaysExcludeItem = new ToolStripMenuItem("Always Exclude")
        {
            ToolTipText = "Persist exclusion rules per process — auto-exclude future windows.",
        };
        menu.Items.Add(_alwaysExcludeItem);

        // Restore All: un-exclude every window that AltTabExcluder excluded.
        var restoreAllItem = new ToolStripMenuItem("Restore All")
        {
            ToolTipText = "Un-exclude all windows that AltTabExcluder has hidden from Alt+Tab.",
        };
        restoreAllItem.Click += (_, _) => RestoreAllRequested?.Invoke(this, EventArgs.Empty);
        menu.Items.Add(restoreAllItem);

        menu.Items.Add(new ToolStripSeparator());

        // Settings submenu: hotkey toggle, change hotkey, startup, restart-as-admin.
        var settingsItem = new ToolStripMenuItem("Settings");

        // Hotkey section: enable/disable toggle + change hotkey.
        _hotkeyItem = new ToolStripMenuItem("Hotkey: Win+Alt+X (toggle focused)")
        {
            Checked = true,
            CheckOnClick = false,
            ToolTipText = "Enable or disable the global hotkey.",
        };
        _hotkeyItem.Click += (_, _) =>
        {
            _hotkeyItem.Checked = !_hotkeyItem.Checked;
            HotkeyToggleRequested?.Invoke(this, _hotkeyItem.Checked);
        };
        settingsItem.DropDownItems.Add(_hotkeyItem);

        var changeHotkeyItem = new ToolStripMenuItem("Change Hotkey...")
        {
            ToolTipText = "Set a custom key combination for the toggle hotkey.",
        };
        changeHotkeyItem.Click += (_, _) => ChangeHotkeyRequested?.Invoke(this, EventArgs.Empty);
        settingsItem.DropDownItems.Add(changeHotkeyItem);

        settingsItem.DropDownItems.Add(new ToolStripSeparator());

        // Run at Windows startup (HKCU\...\Run).
        var startupItem = new ToolStripMenuItem("Run at Windows startup")
        {
            CheckOnClick = true,
            ToolTipText = "Launch AltTabExcluder automatically when you sign in.",
        };
        startupItem.Click += (_, _) => StartupToggleRequested?.Invoke(this, startupItem.Checked);
        _startupItem = startupItem;
        settingsItem.DropDownItems.Add(_startupItem);

        var restartAdminItem = new ToolStripMenuItem("Restart as Administrator");
        restartAdminItem.Click += (_, _) => RestartAsAdministrator();
        settingsItem.DropDownItems.Add(restartAdminItem);

        menu.Items.Add(settingsItem);

        // About: opens the About dialog with app info, how-it-works, and credit.
        var aboutItem = new ToolStripMenuItem("About...");
        aboutItem.Click += (_, _) => AboutRequested?.Invoke(this, EventArgs.Empty);
        menu.Items.Add(aboutItem);

        var exitItem = new ToolStripMenuItem("Exit");
        exitItem.Click += (_, _) => ExitRequested?.Invoke(this, EventArgs.Empty);
        menu.Items.Add(exitItem);

        _notifyIcon.ContextMenuStrip = menu;

        // Populate submenus every time the main menu opens so lists are current.
        // Enumerate once and share between both submenus to avoid double
        // EnumWindows + process lookups on every menu open.
        menu.Opening += (_, _) =>
        {
            _exclusion.PruneStale();
            var windows = WindowEnumerationService.GetOpenWindows(_exclusion);
            PopulateQuickExclude(windows);
            PopulateAlwaysExclude(windows);
        };

        // Keep the menu open when an exclude item is clicked, but allow other
        // items (Exit, Restart, etc.) to close normally.
        menu.Closing += (_, e) =>
        {
            if (e.CloseReason == ToolStripDropDownCloseReason.ItemClicked && _excludeClicked)
            {
                _excludeClicked = false;
                e.Cancel = true;
            }
        };
    }

    // ─── Quick Exclude: live window toggles ──────────────────────────────

    /// <summary>
    /// Enumerates all open visible windows and fills the Quick Exclude submenu.
    /// Windows excluded by AltTabExcluder show a checkmark and can be toggled
    /// back. Windows that naturally have WS_EX_TOOLWINDOW (tool palettes, helper
    /// windows) show a checkmark but are greyed out — they weren't excluded by
    /// us, so toggling them is disabled to avoid the double-click issue.
    /// </summary>
    private void PopulateQuickExclude(IReadOnlyList<WindowInfo> windows)
    {
        ClearSubmenu(_quickExcludeItem);

        if (windows.Count == 0)
        {
            _quickExcludeItem.DropDownItems.Add("(no open windows)").Enabled = false;
            return;
        }

        foreach (var w in windows)
        {
            // Query live exclusion state — the WindowInfo snapshot may be stale
            // if we're refreshing after an Always Exclude click changed styles.
            // IsExcluded is a single GetWindowLongPtr P/Invoke (cheap).
            bool isExcluded = _exclusion.IsExcluded(w.Hwnd);
            bool wasExcludedByUs = _exclusion.WasExcludedByUs(w.Hwnd);

            string title = w.WindowTitle;
            if (title.Length > 50)
                title = title[..47] + "...";

            var item = new ToolStripMenuItem($"{w.ProcessName} — {title}")
            {
                Checked = isExcluded,
                Tag = w.Hwnd,
            };

            if (isExcluded && !wasExcludedByUs)
            {
                // Naturally a tool window — not excluded by us. Disable toggling.
                item.Enabled = false;
                item.ToolTipText = "Not excluded by AltTabExcluder (natural tool window).";
            }
            else
            {
                item.ToolTipText = w.WindowTitle;
            }

            if (w.ProcessIcon is not null)
            {
                try
                {
                    using var sized = new Icon(w.ProcessIcon, 16, 16);
                    item.Image = sized.ToBitmap();
                }
                catch (Exception ex)
                {
                    AppLogger.LogDebug($"Failed to render tray icon for {w.ProcessName}: {ex.Message}");
                }
            }

            item.Click += OnQuickExcludeItemClicked;
            _quickExcludeItem.DropDownItems.Add(item);
        }

        _quickExcludeItem.DropDown.Closing += ExcludeDropDown_Closing;
    }

    private void OnQuickExcludeItemClicked(object? sender, EventArgs e)
    {
        if (sender is not ToolStripMenuItem item || item.Tag is not IntPtr hwnd)
            return;

        // UIPI blocks style changes on elevated target windows when we're not
        // elevated. Detect this up front and warn the user rather than silently
        // doing nothing.
        if (ElevationDetector.IsWindowElevated(hwnd) && !ElevationDetector.IsCurrentProcessElevated())
        {
            ShowNotification("AltTabExcluder — elevation required",
                "That window is running as Administrator. Restart AltTabExcluder as Administrator (tray menu) to toggle it.",
                ToolTipIcon.Warning);
            _excludeClicked = true;
            return;
        }

        try
        {
            _exclusion.Toggle(hwnd);
            item.Checked = _exclusion.IsExcluded(hwnd);
        }
        catch (Exception ex)
        {
            AppLogger.LogWarning(ex, $"Quick exclude toggle failed for HWND {hwnd}");
        }
        _excludeClicked = true;
    }

    // ─── Always Exclude: persistent per-process rules ────────────────────

    /// <summary>
    /// Fills the Always Exclude submenu. First lists all currently running
    /// processes (with icons), then adds a separator and lists any saved rules
    /// for processes that aren't currently running — so users can see and
    /// remove persistent rules even when the app is closed.
    /// </summary>
    private void PopulateAlwaysExclude(IReadOnlyList<WindowInfo> windows)
    {
        ClearSubmenu(_alwaysExcludeItem);

        // Collect unique process names from currently open windows.
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var entries = new List<(string Name, Icon? Icon)>();

        foreach (var w in windows)
        {
            if (seen.Add(w.ProcessName))
                entries.Add((w.ProcessName, w.ProcessIcon));
        }

        if (entries.Count == 0)
        {
            _alwaysExcludeItem.DropDownItems.Add("(no open windows)").Enabled = false;
        }
        else
        {
            foreach (var (name, icon) in entries)
            {
                var rule = _rules.GetRule(name);
                var item = new ToolStripMenuItem(name)
                {
                    Checked = rule is not null && rule.Exclude,
                    Tag = name,
                    ToolTipText = "Auto-exclude all future windows from this process.",
                };

                if (icon is not null)
                {
                    try
                    {
                        using var sized = new Icon(icon, 16, 16);
                        item.Image = sized.ToBitmap();
                    }
                    catch (Exception ex)
                    {
                        AppLogger.LogDebug($"Failed to render tray icon for {name}: {ex.Message}");
                    }
                }

                item.Click += OnAlwaysExcludeItemClicked;
                _alwaysExcludeItem.DropDownItems.Add(item);
            }
        }

        // Add saved rules for processes that aren't currently running.
        var savedRules = _rules.Rules;
        var notRunning = savedRules
            .Where(r => !seen.Contains(r.ProcessName))
            .ToList();

        if (notRunning.Count > 0)
        {
            _alwaysExcludeItem.DropDownItems.Add(new ToolStripSeparator());

            var header = new ToolStripLabel("(saved rules — not running)")
            {
                Enabled = false,
            };
            _alwaysExcludeItem.DropDownItems.Add(header);

            foreach (var rule in notRunning)
            {
                var item = new ToolStripMenuItem(rule.ProcessName)
                {
                    Checked = rule.Exclude,
                    Tag = rule.ProcessName,
                    ToolTipText = "Saved rule — process is not currently running.",
                };
                item.Click += OnAlwaysExcludeItemClicked;
                _alwaysExcludeItem.DropDownItems.Add(item);
            }
        }

        _alwaysExcludeItem.DropDown.Closing += ExcludeDropDown_Closing;
    }

    private void OnAlwaysExcludeItemClicked(object? sender, EventArgs e)
    {
        if (sender is not ToolStripMenuItem item || item.Tag is not string procName)
            return;

        bool newState = !item.Checked;
        _rules.SetRule(procName, newState);

        // Apply to currently open windows of this process. Enumerate once and
        // reuse the list for both the apply pass and the Quick Exclude refresh
        // below — avoids a second EnumWindows + process-lookup pass.
        var windows = WindowEnumerationService.GetOpenWindows(_exclusion);
        bool weAreElevated = ElevationDetector.IsCurrentProcessElevated();
        int skipped = 0;

        foreach (var w in windows)
        {
            if (!string.Equals(w.ProcessName, procName, StringComparison.OrdinalIgnoreCase))
                continue;

            // Skip elevated targets when we're not elevated — UIPI blocks the
            // style change.
            if (!weAreElevated && ElevationDetector.IsWindowElevated(w.Hwnd))
            {
                skipped++;
                continue;
            }

            if (newState)
            {
                // Excluding: always safe to apply.
                _exclusion.SetExcluded(w.Hwnd, true);
            }
            else
            {
                // Un-excluding: only strip WS_EX_TOOLWINDOW from windows we
                // excluded — don't touch natural tool windows.
                if (w.WasExcludedByUs)
                    _exclusion.SetExcluded(w.Hwnd, false);
            }
        }

        item.Checked = newState;

        if (skipped > 0)
        {
            ShowNotification("AltTabExcluder — elevation required",
                $"{skipped} elevated window(s) of '{procName}' could not be toggled. Restart as Administrator to manage them.",
                ToolTipIcon.Warning);
        }

        // Refresh Quick Exclude so its checkmarks reflect the windows we just
        // excluded/un-excluded. Reuse the already-enumerated list instead of
        // calling GetOpenWindows() a second time.
        PopulateQuickExclude(windows);

        _excludeClicked = true;
    }

    // ─── Shared helpers ──────────────────────────────────────────────────

    /// <summary>
    /// Prevents an exclude submenu from closing when an item is clicked — the
    /// user can toggle multiple items, then click away to dismiss.
    /// </summary>
    private void ExcludeDropDown_Closing(object? sender, ToolStripDropDownClosingEventArgs e)
    {
        if (e.CloseReason == ToolStripDropDownCloseReason.ItemClicked)
            e.Cancel = true;
    }

    /// <summary>Disposes and clears a submenu's items safely.</summary>
    private static void ClearSubmenu(ToolStripMenuItem parent)
    {
        var oldItems = new List<ToolStripItem>(parent.DropDownItems.Count);
        foreach (ToolStripItem item in parent.DropDownItems)
            oldItems.Add(item);
        parent.DropDownItems.Clear();
        foreach (var item in oldItems)
        {
            item.Image?.Dispose();
            item.Dispose();
        }
    }

    /// <summary>Updates the hotkey menu item's checked state.</summary>
    public void SetHotkeyChecked(bool registered) => _hotkeyItem.Checked = registered;

    /// <summary>Updates the hotkey menu item's label to reflect the current combo.</summary>
    public void SetHotkeyLabel(string label)
        => _hotkeyItem.Text = $"Hotkey: {label} (toggle focused)";

    /// <summary>Updates the startup menu item's checked state.</summary>
    public void SetStartupChecked(bool enabled) => _startupItem.Checked = enabled;

    /// <summary>Updates the tray icon hover tooltip to show the current hotkey.</summary>
    public void SetTrayTooltip(string hotkeyLabel)
        => _notifyIcon.Text = $"AltTabExcluder — {hotkeyLabel}";

    private static Icon LoadTrayIcon()
    {
        try
        {
            var asm = System.Reflection.Assembly.GetExecutingAssembly();
            using var stream = asm.GetManifestResourceStream("AltTabExcluder.assets.app.ico");
            if (stream is not null)
                return new Icon(stream);
        }
        catch (Exception ex)
        {
            AppLogger.LogDebug($"Failed to load embedded tray icon: {ex.Message}");
        }

        // Fallback: load from disk (development / non-embedded scenario).
        string path = Path.Combine(AppContext.BaseDirectory, "assets", "app.ico");
        if (File.Exists(path))
        {
            try { return new Icon(path); }
            catch (Exception ex) { AppLogger.LogDebug($"Failed to load tray icon from disk: {ex.Message}"); }
        }
        return SystemIcons.Application;
    }

    /// <summary>Shows a balloon tip notification.</summary>
    public void ShowNotification(string title, string message, ToolTipIcon icon = ToolTipIcon.Info)
        => _notifyIcon.ShowBalloonTip(3000, title, message, icon);

    /// <summary>
    /// Raises the <see cref="RestartRequested"/> event. The actual process
    /// restart (with UAC elevation) is handled by
    /// <see cref="TrayEventCoordinator.OnRestartRequested"/>, which releases
    /// the single-instance mutex before starting the new process so the
    /// elevated instance can acquire it.
    /// </summary>
    private void RestartAsAdministrator()
    {
        RestartRequested?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        _notifyIcon.Visible = false;
        _notifyIcon.ContextMenuStrip?.Dispose();
        _notifyIcon.Dispose();
    }
}
