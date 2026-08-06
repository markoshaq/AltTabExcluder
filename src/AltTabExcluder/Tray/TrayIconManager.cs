using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using AltTabExcluder.Services;

namespace AltTabExcluder.Tray;

/// <summary>
/// Owns the system-tray <see cref="NotifyIcon"/> and its context menu. All user
/// interaction happens here: Quick Exclude (toggle live windows), Always Exclude
/// (persist rules per process), hotkey toggle, startup toggle, restart-as-admin,
/// and exit.
/// </summary>
public sealed class TrayIconManager : IDisposable
{
    private readonly NotifyIcon _notifyIcon;
    private readonly ToolStripMenuItem _hotkeyItem;
    private readonly ToolStripMenuItem _quickExcludeItem;
    private readonly ToolStripMenuItem _alwaysExcludeItem;
    private readonly ToolStripMenuItem _startupItem;
    private readonly ToolStripMenuItem _restartAdminItem;
    private readonly ToolStripMenuItem _exitItem;
    private readonly RuleEngine _rules;

    /// <summary>Flag set when a Quick/Always Exclude item is clicked, so the
    /// parent menu's Closing handler knows to keep the menu open.</summary>
    private bool _excludeClicked;

    public event EventHandler? ExitRequested;

    /// <summary>Raised when the user toggles the global hotkey on/off via tray.</summary>
    public event EventHandler<bool>? HotkeyToggleRequested;

    /// <summary>Raised when the user wants to change the hotkey. Payload is an
    /// action that receives the current (modifiers, key) and returns the new
    /// (modifiers, key) or null if cancelled.</summary>
    public event EventHandler? ChangeHotkeyRequested;

    /// <summary>Raised when the user toggles "Run at Windows startup".</summary>
    public event EventHandler<bool>? StartupToggleRequested;

    /// <summary>Raised when the user wants to restart as Administrator.</summary>
    public event EventHandler? RestartRequested;

    public TrayIconManager(RuleEngine rules)
    {
        _rules = rules;

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

        menu.Items.Add(new ToolStripSeparator());

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
        menu.Items.Add(_hotkeyItem);

        var changeHotkeyItem = new ToolStripMenuItem("Change Hotkey...")
        {
            ToolTipText = "Set a custom key combination for the toggle hotkey.",
        };
        changeHotkeyItem.Click += (_, _) => ChangeHotkeyRequested?.Invoke(this, EventArgs.Empty);
        menu.Items.Add(changeHotkeyItem);

        menu.Items.Add(new ToolStripSeparator());

        // Phase 4: run at Windows startup (HKCU\...\Run).
        var startupItem = new ToolStripMenuItem("Run at Windows startup")
        {
            CheckOnClick = true,
            ToolTipText = "Launch AltTabExcluder automatically when you sign in.",
        };
        startupItem.Click += (_, _) => StartupToggleRequested?.Invoke(this, startupItem.Checked);
        _startupItem = startupItem;

        menu.Items.Add(_startupItem);
        _restartAdminItem = new ToolStripMenuItem("Restart as Administrator");
        _restartAdminItem.Click += (_, _) => RestartAsAdministrator();
        menu.Items.Add(_restartAdminItem);

        menu.Items.Add(new ToolStripSeparator());

        _exitItem = new ToolStripMenuItem("Exit");
        _exitItem.Click += (_, _) => ExitRequested?.Invoke(this, EventArgs.Empty);
        menu.Items.Add(_exitItem);

        _notifyIcon.ContextMenuStrip = menu;

        // Populate submenus every time the main menu opens so lists are current.
        menu.Opening += (_, _) =>
        {
            WindowManager.PruneStaleHandles();
            PopulateQuickExclude();
            PopulateAlwaysExclude();
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
    private void PopulateQuickExclude()
    {
        ClearSubmenu(_quickExcludeItem);

        var windows = WindowEnumerationService.GetOpenWindows();
        if (windows.Count == 0)
        {
            _quickExcludeItem.DropDownItems.Add("(no open windows)").Enabled = false;
            return;
        }

        foreach (var w in windows)
        {
            string title = w.WindowTitle;
            if (title.Length > 50)
                title = title[..47] + "...";

            var item = new ToolStripMenuItem($"{w.ProcessName} — {title}")
            {
                Checked = w.IsExcluded,
                Tag = w.Hwnd,
            };

            if (w.IsExcluded && !w.WasExcludedByUs)
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
                catch { /* icon unusable */ }
            }

            item.Click += OnQuickExcludeItemClicked;
            _quickExcludeItem.DropDownItems.Add(item);
            w.ProcessIcon?.Dispose();
        }

        _quickExcludeItem.DropDown.Closing += ExcludeDropDown_Closing;
    }

    private void OnQuickExcludeItemClicked(object? sender, EventArgs e)
    {
        if (sender is not ToolStripMenuItem item || item.Tag is not IntPtr hwnd)
            return;

        try
        {
            WindowManager.ToggleAltTabVisibility(hwnd);
            item.Checked = WindowManager.IsWindowExcluded(hwnd);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Quick exclude toggle failed: {ex.Message}");
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
    private void PopulateAlwaysExclude()
    {
        ClearSubmenu(_alwaysExcludeItem);

        // Collect unique process names from currently open windows.
        var windows = WindowEnumerationService.GetOpenWindows();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var entries = new List<(string Name, Icon? Icon)>();

        foreach (var w in windows)
        {
            if (seen.Add(w.ProcessName))
                entries.Add((w.ProcessName, w.ProcessIcon));
            else
                w.ProcessIcon?.Dispose();
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
                    catch { /* icon unusable */ }
                    icon?.Dispose();
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

        // Apply to currently open windows of this process.
        var windows = WindowEnumerationService.GetOpenWindows();
        foreach (var w in windows)
        {
            if (!string.Equals(w.ProcessName, procName, StringComparison.OrdinalIgnoreCase))
                continue;

            if (newState)
            {
                // Excluding: always safe to apply.
                WindowManager.SetExcluded(w.Hwnd, true);
            }
            else
            {
                // Un-excluding: only strip WS_EX_TOOLWINDOW from windows we
                // excluded — don't touch natural tool windows.
                if (w.WasExcludedByUs)
                    WindowManager.SetExcluded(w.Hwnd, false);
            }
        }

        item.Checked = newState;
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

    private static Icon LoadTrayIcon()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "assets", "app.ico");
        if (File.Exists(path))
        {
            try { return new Icon(path); }
            catch { /* fall through to default */ }
        }
        return SystemIcons.Application;
    }

    /// <summary>Shows a balloon tip notification.</summary>
    public void ShowNotification(string title, string message, ToolTipIcon icon = ToolTipIcon.Info)
        => _notifyIcon.ShowBalloonTip(3000, title, message, icon);

    private void RestartAsAdministrator()
    {
        try
        {
            var exe = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exe))
                return;

            var psi = new ProcessStartInfo
            {
                FileName = exe,
                UseShellExecute = true,
                Verb = "runas",
            };
            Process.Start(psi);
            RestartRequested?.Invoke(this, EventArgs.Empty);
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // User declined the UAC prompt.
        }
    }

    public void Dispose()
    {
        _notifyIcon.Visible = false;
        _notifyIcon.ContextMenuStrip?.Dispose();
        _notifyIcon.Dispose();
    }
}
