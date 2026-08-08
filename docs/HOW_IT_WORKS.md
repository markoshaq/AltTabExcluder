# How It Works

AltTabExcluder toggles two Win32 extended window styles on target windows:

| Style | Effect |
|-------|--------|
| `WS_EX_TOOLWINDOW` | Hides the window from Alt+Tab and the taskbar |
| `WS_EX_APPWINDOW` | Forces the window onto the taskbar/Alt+Tab |

To **exclude** a window: set `WS_EX_TOOLWINDOW`, clear `WS_EX_APPWINDOW`.
To **restore** it: clear `WS_EX_TOOLWINDOW`, set `WS_EX_APPWINDOW`.

After a style change, a `SetWindowPos(SWP_FRAMECHANGED)` call forces the
shell to re-evaluate the window's taskbar/Alt+Tab presence immediately.

The app uses [CsWin32](https://github.com/microsoft/CsWin32) for type-safe
Win32 P/Invoke bindings, generated from `NativeMethods.txt`.

## Auto-apply mechanism

For **Always Exclude** rules, the app installs a `SetWinEventHook` for
`EVENT_OBJECT_CREATE` and `EVENT_OBJECT_SHOW` (out-of-context, skipping its
own process). When a new top-level window appears that matches a saved rule,
the style is applied automatically — no DLL injection, callbacks arrive on
the UI thread via the message loop. Child windows are filtered out via
`GetAncestor(GA_ROOT)` so rules are only applied to top-level windows.

At startup, a one-time sweep applies existing rules to windows that were
already open before AltTabExcluder launched (the WinEvent hook only covers
windows created after it is installed).

## HWND recycling mitigation

The excluded-by-us tracking set persists `(HWND, PID)` pairs. After a window
closes, the OS can recycle the HWND value for a new, unrelated window. By
comparing the stored PID with the window's current PID, AltTabExcluder detects
recycled HWNDs and treats them as stale.

Pruning happens at four points:
- **On startup** — cleans up windows closed since the last session.
- **On tray menu open** — ensures the Quick Exclude list is current.
- **On RestoreAll** — skips recycled HWNDs before restoring.
- **Periodically (every 60s)** — catches stale entries even if the user
  never opens the menu.

The per-process rule system (Always Exclude) is the more robust mechanism for
persistent exclusion, as it keys on process name rather than window handles.

## Storage

| File | Contents |
|------|----------|
| `%APPDATA%\AltTabExcluder\settings.json` | Hotkey configuration, hotkey enabled state, excluded-by-us HWND+PID tracking |
| `%APPDATA%\AltTabExcluder\rules.json` | Persistent per-process exclusion rules |
| `%APPDATA%\AltTabExcluder\app.log` | Rotating log file (256 KB, with `.bak` backup) |

All files are written atomically (temp file + move) so a crash can't
corrupt them, and loaded fault-tolerantly (a corrupt file yields defaults
rather than crashing the app).

## Tech Stack

- **.NET 8** (`net8.0-windows`, x64)
- **WinForms** — tray icon, context menus, hotkey picker, about dialog
- **CsWin32** — source-generated Win32 P/Invoke bindings
- **xUnit + coverlet** — unit tests with per-file code coverage gate in CI
- **No WPF, no third-party dependencies** (beyond test tooling)

For the full architecture, see [ARCHITECTURE.md](ARCHITECTURE.md).
