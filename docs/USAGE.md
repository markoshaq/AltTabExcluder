# Usage Guide

Detailed instructions for using AltTabExcluder. For a quick overview, see the
[README](../README.md).

## Hide the focused window

1. Focus the window you want to exclude.
2. Press `Win+Alt+X` (or your custom hotkey).
3. The window disappears from Alt+Tab. Press the hotkey again to restore it.

## Hide a specific window from the tray

1. Right-click the tray icon.
2. Go to **Quick Exclude**.
3. Click any window in the list to toggle its Alt+Tab visibility.
   - Windows with a checkmark are currently hidden.
   - Greyed-out items are natural tool windows (not excluded by
     AltTabExcluder) and can't be toggled.
   - The submenu stays open after a click so you can toggle multiple windows.

## Always exclude an app

1. Right-click the tray icon.
2. Go to **Always Exclude**.
3. Click a process name to create a persistent rule.
   - A checkmark means future windows from this app will be auto-excluded.
   - Click again to remove the rule.
4. Saved rules for apps that aren't currently running appear under a
   separator at the bottom — you can remove them there.

## Restore all excluded windows

1. Right-click the tray icon.
2. Click **Restore All**.
3. Every window that AltTabExcluder has hidden is restored to Alt+Tab at
   once. A notification shows how many windows were restored.

## Change the hotkey

1. Right-click the tray icon &rarr; **Settings** &rarr; **Change Hotkey...**
2. Press your desired key combination in the dialog. At least one modifier
   (Ctrl/Alt/Shift/Win) is required.
3. Click **OK**. If the combination is already in use by another app,
   you'll get a warning and the old hotkey is restored.

## Enable or disable the hotkey

1. Right-click the tray icon &rarr; **Settings**.
2. Click the **Hotkey** item to toggle it on or off.
3. The state is saved — if you disable the hotkey, it stays disabled on the
   next launch.

## Toggle elevated windows

If the focused window belongs to an Administrator-elevated process and
AltTabExcluder is running normally, the hotkey won't be able to change its
style (UIPI blocks it). You'll get a notification prompting you to
**Restart as Administrator** from the tray menu. After restarting, the
hotkey works on elevated windows too.

## Run at Windows startup

1. Right-click the tray icon &rarr; **Settings**.
2. Click **Run at Windows startup** to toggle it on or off.
3. This uses the per-user `HKCU\...\Run` key — no admin privileges required.
