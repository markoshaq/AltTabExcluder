<div align="center">

# AltTabExcluder

**Keep clutter out of your Alt+Tab switcher.**

A lightweight Windows system-tray utility that lets you hide any window
from the Alt+Tab switcher — and optionally remember that choice so future
windows from the same app are excluded automatically.

[Features](#features) &nbsp;&middot;&nbsp; [Install](#install) &nbsp;&middot;&nbsp; [Usage](#usage) &nbsp;&middot;&nbsp; [Build](#build-from-source) &nbsp;&middot;&nbsp; [How It Works](#how-it-works)

</div>

---

AltTabExcluder runs quietly in your system tray. Press a hotkey to hide the
currently focused window from Alt+Tab, or open the tray menu to pick from a
list of all open windows. Want Spotify, Discord, or your email client to
*always* stay out of the switcher? Toggle **Always Exclude** once and every
future window from that app is hidden automatically.

It works by toggling the native Win32 `WS_EX_TOOLWINDOW` /
`WS_EX_APPWINDOW` extended window styles — no injection, no DLL hooking,
no background process eating memory. Just a tiny tray app that talks to the
window manager.

## Features

- **Hotkey toggle** — press `Win+Alt+X` (customizable) to instantly hide or
  restore the focused window from Alt+Tab.
- **Quick Exclude** — browse all open windows from the tray menu and toggle
  each one with a click. Checkmarks show current state.
- **Always Exclude** — save a per-process rule so every future window from
  that app is auto-excluded the moment it opens. No need to re-toggle each
  time you launch the app.
- **Custom hotkey picker** — change the toggle shortcut to any combination
  (Win/Alt/Ctrl/Shift + any key) via a dedicated picker dialog.
- **Run at startup** — optional, per-user, no admin privileges required.
- **Elevation-aware** — detects when a target window is running as
  Administrator and offers a one-click "Restart as Administrator" so you can
  toggle elevated windows too.
- **Tray-only** — no main window, no taskbar button, no clutter. Just an icon
  in the notification area.
- **Persistent** — your rules and hotkey setting survive restarts.

## Install

> Pre-built binaries are not yet published. For now, [build from source](#build-from-source).

### Build from source

Requires the **.NET 8 SDK** and Windows 10/11 (x64).

```powershell
git clone <repo-url>
cd AltTabExcluder
dotnet build -c Release
```

The executable will be at
`src\AltTabExcluder\bin\x64\Release\net8.0-windows\AltTabExcluder.exe`.

Run it directly, or use the tray menu's **Run at Windows startup** option to
have it launch automatically on sign-in.

## Usage

### Hide the focused window

1. Focus the window you want to exclude.
2. Press `Win+Alt+X` (or your custom hotkey).
3. The window disappears from Alt+Tab. Press the hotkey again to restore it.

### Hide a specific window from the tray

1. Right-click the tray icon.
2. Go to **Quick Exclude**.
3. Click any window in the list to toggle its Alt+Tab visibility.
   - Windows with a checkmark are currently hidden.
   - Greyed-out items are natural tool windows (not excluded by
     AltTabExcluder) and can't be toggled.

### Always exclude an app

1. Right-click the tray icon.
2. Go to **Always Exclude**.
3. Click a process name to create a persistent rule.
   - A checkmark means future windows from this app will be auto-excluded.
   - Click again to remove the rule.
4. Saved rules for apps that aren't currently running appear under a
   separator at the bottom — you can remove them there.

### Change the hotkey

1. Right-click the tray icon &rarr; **Change Hotkey...**
2. Press your desired key combination in the dialog.
3. Click **OK**. If the combination is already in use by another app,
   you'll get a warning and the old hotkey is restored.

### Toggle elevated windows

If the focused window belongs to an Administrator-elevated process and
AltTabExcluder is running normally, the hotkey won't be able to change its
style (UIPI blocks it). You'll get a notification prompting you to
**Restart as Administrator** from the tray menu. After restarting, the
hotkey works on elevated windows too.

## How It Works

AltTabExcluder toggles two Win32 extended window styles on target windows:

| Style | Effect |
|-------|--------|
| `WS_EX_TOOLWINDOW` | Hides the window from Alt+Tab and the taskbar |
| `WS_EX_APPWINDOW` | Forces the window onto the taskbar/Alt+Tab |

To **exclude** a window: set `WS_EX_TOOLWINDOW`, clear `WS_EX_APPWINDOW`.
To **restore** it: clear `WS_EX_TOOLWINDOW`, set `WS_EX_APPWINDOW`.

These style changes take effect immediately for the shell's task switcher.
The app uses [CsWin32](https://github.com/microsoft/CsWin32) for type-safe
Win32 P/Invoke bindings, generated from `NativeMethods.txt`.

### Auto-apply mechanism

For **Always Exclude** rules, the app installs a `SetWinEventHook` for
`EVENT_OBJECT_CREATE` (out-of-context, skipping its own process). When a new
top-level window appears that matches a saved rule, the style is applied
automatically — no DLL injection, callbacks arrive on the UI thread via the
message loop.

### Storage

| File | Contents |
|------|----------|
| `%APPDATA%\AltTabExcluder\settings.json` | Hotkey configuration, excluded-by-us HWND tracking |
| `%APPDATA%\AltTabExcluder\rules.json` | Persistent per-process exclusion rules |

Both files are written atomically (temp file + move) so a crash can't
corrupt them, and loaded fault-tolerantly (a corrupt file yields defaults
rather than crashing the app).

## Tech Stack

- **.NET 8** (`net8.0-windows`, x64)
- **WinForms** — tray icon, context menus, hotkey picker dialog
- **CsWin32** — source-generated Win32 P/Invoke bindings
- **No WPF, no third-party dependencies**

## Limitations

- **Elevated windows** require AltTabExcluder to also run elevated (UIPI).
  The app detects this and offers a restart-as-admin action.
- **Child windows** are not listed in Quick Exclude — only top-level
  windows with a title.
- **HWND recycling**: the excluded-by-us tracking set persists HWND values,
  which the OS can recycle after a window closes. This is a known
  limitation; the per-process rule system is the more robust mechanism.

## License

MIT
