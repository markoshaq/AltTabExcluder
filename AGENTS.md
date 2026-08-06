# AltTabExcluder

Lightweight Windows system-tray utility (.NET 8, WinForms) that excludes
specific running windows from the Alt+Tab switcher by toggling the Win32
`WS_EX_TOOLWINDOW` / `WS_EX_APPWINDOW` extended styles on target window
handles.

The app is **tray-only**: there is no main window or dashboard. All user
interaction happens through the tray context menu and a configurable global
hotkey.

## Status

Implemented and feature-complete for the tray-only design:

- Core window-style toggle (`WS_EX_TOOLWINDOW` / `WS_EX_APPWINDOW`).
- Tray context menu with Quick Exclude (live per-window toggles) and Always
  Exclude (persistent per-process rules).
- Configurable global hotkey (default `Win+Alt+X`) with a hotkey-picker dialog.
- Per-process rule persistence (`rules.json`) with auto-apply to newly created
  windows via `SetWinEventHook`.
- Run-at-Windows-startup toggle (HKCU `\Run`).
- Elevation detection with "Restart as Administrator" fallback.
- Settings persistence (`settings.json`) for hotkey config and the
  excluded-by-us HWND tracking set.

The project has **not yet been compiled** — the dev machine it was scaffolded
on has no .NET SDK installed (only the 8.0.14 / 9.0.10 runtimes). The first
build on a machine with the .NET 8 SDK will validate the CsWin32-generated
signatures listed below.

## Build & run

> Install the .NET 8 SDK first, then:

```powershell
dotnet restore
dotnet build -c Debug
dotnet run --project src\AltTabExcluder
```

Target framework is `net8.0-windows`, x64 only (so it can interact with
64-bit target processes). **WinForms only** (`UseWindowsForms=true`) — WPF is
not enabled. WinForms provides the `NotifyIcon` tray control, the context
menu, and the hotkey-picker dialog. `AllowUnsafeBlocks` is enabled for
CsWin32 pointer parameters. DPI awareness is `SystemAware` (set via the
`ApplicationHighDpiMode` project property, not the manifest).

## Architecture

### Entry point

- `Program.cs` — WinForms entry point. `ApplicationConfiguration.Initialize()`
  + `Application.Run()` keeps the process alive with only the tray icon
  present. `Program` is a static coordinator that owns and wires up all
  services (`_tray`, `_hotkey`, `_rules`, `_watcher`, `_settings`) and handles
  their events. `ShutdownApp()` disposes services in order and calls
  `Application.Exit()`.

### Core logic

- `WindowManager.cs` — static class with the core Win32 style logic:
  `IsWindowExcluded`, `WasExcludedByUs`, `ToggleAltTabVisibility`,
  `SetExcluded`, `PruneStaleHandles`. Maintains a static `HashSet<IntPtr>`
  (`ExcludedByUs`) tracking which HWNDs were excluded by AltTabExcluder
  (vs. windows that naturally carry `WS_EX_TOOLWINDOW`). This set is
  persisted via `AppSettings` so Quick Exclude can identify our exclusions
  after restart.

### Services (`Services/`)

- `WindowEnumerationService.cs` — `EnumWindows`-based snapshot of open
  visible top-level windows, enriched with process name + icon. Filters out
  the desktop, taskbar, IME, and the app's own windows.
- `WindowInfo.cs` — immutable record describing one enumerated window
  (HWND, PID, process name, title, icon, exclusion state).
- `HotkeyManager.cs` — registers a configurable global hotkey (default
  `Win+Alt+X`) via `RegisterHotKey` on a hidden message-only `NativeWindow`
  (`HWND_MESSAGE`). The primary cross-process toggle mechanism. Implements
  `IDisposable`.
- `RuleEngine.cs` — JSON persistence to
  `%APPDATA%\AltTabExcluder\rules.json` (atomic temp+move write),
  query/upsert/remove APIs, and `ApplyTo(hwnd, processName)`. Rules are keyed
  by process name (case-insensitive, no extension). Fault-tolerant loading.
- `ProcessRule.cs` — record modelling one persisted per-process rule
  (`ProcessName`, `Exclude`, `CreatedAt`).
- `WindowEventWatcher.cs` — `SetWinEventHook(EVENT_OBJECT_CREATE)` with
  `WINEVENT_OUTOFCONTEXT | WINEVENT_SKIPOWNPROCESS`; auto-applies matching
  rules to newly created windows. Uses a one-shot `WinForms.Timer` to
  re-check windows that aren't yet visible/titled when the event fires.
  Implements `IDisposable`.
- `AppSettings.cs` — persists to `%APPDATA%\AltTabExcluder\settings.json`.
  Holds the hotkey configuration (modifiers + key code) and the
  `ExcludedByUs` HWND list. Provides `FormatHotkey` for human-readable
  labels. Atomic write, fault-tolerant load.
- `StartupManager.cs` — `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`
  toggle for "Run at Windows startup" (per-user, no admin needed).
- `ElevationDetector.cs` — token-based elevation check for the current and
  target processes. Uses `NativeElevationInterop` (raw P/Invoke, not CsWin32)
  for `OpenProcess` / `OpenProcessToken` / `GetTokenInformation`.
- `NativeElevationInterop.cs` — raw `DllImport` P/Invoke for process/token
  elevation queries. Deliberately avoids CsWin32 for these APIs to sidestep
  the `SafeHandle` / `TOKEN_ACCESS_MASK` / `PROCESS_ACCESS_RIGHTS` types
  that are cumbersome for the simple elevation check.

### Tray (`Tray/`)

- `TrayIconManager.cs` — owns the `NotifyIcon` and its context menu. Menu
  items: **Quick Exclude** (submenu of all open windows with live toggle
  checkmarks), **Always Exclude** (submenu of running processes + saved
  rules, with persistent per-process toggle), **Hotkey** (enable/disable
  toggle), **Change Hotkey...** (opens `HotkeyPickerDialog`), **Run at
  Windows startup** (checkbox), **Restart as Administrator**, **Exit**.
  Submenus are repopulated on every open so lists stay current. Excludes
  submenus stay open after a click so the user can toggle multiple items.
  Implements `IDisposable`.

### UI (`UI/`)

- `HotkeyPickerDialog.cs` — WinForms `Form` that captures a keyboard
  shortcut from the user. Uses a low-level `WH_KEYBOARD_LL` hook (raw
  `DllImport`, not CsWin32) to reliably capture Win-key combinations, which
  Windows intercepts before regular key processing. Returns modifier flags
  + virtual key code.

## Win32 interop (CsWin32)

Bindings are generated by `Microsoft.Windows.CsWin32` (v0.3.298) from
`NativeMethods.txt` into the `Windows.Win32.PInvoke` class. Consuming code
uses the generated `HWND` / `LPARAM` / `PWSTR` / `HWINEVENTHOOK` types.

### APIs actually consumed via CsWin32

- `GetForegroundWindow` — `Program.cs` (hotkey handler).
- `GetWindowLongPtr` / `SetWindowLongPtr` — `WindowManager.cs` (style
  read/write; `nint`-based, pointer-sized).
- `IsWindow` — `WindowManager.cs`, `WindowEventWatcher.cs`.
- `IsWindowVisible` — `WindowEnumerationService.cs`, `WindowEventWatcher.cs`.
- `RegisterHotKey` / `UnregisterHotKey` — `HotkeyManager.cs`.
- `EnumWindows` — `WindowEnumerationService.cs` (takes `ENUM_WINDOW_PROC`
  delegate; lambda returns `true` → `BOOL`).
- `GetWindowText` / `GetWindowTextLength` / `GetClassName` —
  `WindowEnumerationService.cs` (`PWSTR` from `stackalloc char[]`).
- `GetWindowThreadProcessId` — `ElevationDetector.cs`,
  `WindowEnumerationService.cs`, `WindowEventWatcher.cs` (`uint*` via
  `unsafe` block).
- `SetWinEventHook` / `UnhookWinEvent` — `WindowEventWatcher.cs`
  (`WINEVENTPROC` delegate, returns `HWINEVENTHOOK`).

### APIs NOT consumed via CsWin32 (raw DllInvoke instead)

- `OpenProcess` / `OpenProcessToken` / `GetTokenInformation` / `CloseHandle`
  — `NativeElevationInterop.cs` (raw P/Invoke by design; see file doc).
- `SetWindowsHookEx` / `UnhookWindowsHookEx` / `CallNextHookEx` /
  `GetModuleHandle` — `HotkeyPickerDialog.cs` (raw P/Invoke for the
  `WH_KEYBOARD_LL` low-level keyboard hook).

### Unused entries in `NativeMethods.txt`

The following are listed in `NativeMethods.txt` but not referenced by any
current code. They are leftovers from removed/never-built features and can
be deleted to reduce generated code:

- `GetSystemMenu`, `AppendMenu`, `CheckMenuItem` — were for a system-menu
  injection feature that is not implemented.
- `SetWindowsHookEx`, `UnhookWindowsHookEx`, `CallNextHookEx` — CsWin32
  versions unused; `HotkeyPickerDialog` uses its own raw P/Invoke.
- `GetCurrentThreadId` — unused.
- `GetModuleHandle` — CsWin32 version unused; `HotkeyPickerDialog` uses its
  own raw P/Invoke.
- `OpenProcess`, `OpenProcessToken`, `GetTokenInformation`, `CloseHandle` —
  CsWin32 versions unused; `NativeElevationInterop` uses raw P/Invoke.

### Assumptions to verify on first build

1. `GetWindowLongPtr` / `SetWindowLongPtr` are pointer-sized and written to
   use `nint`, which is assignment-compatible whether CsWin32 emits `nint`
   or `IntPtr` for `LONG_PTR`.
2. `GetWindowThreadProcessId`'s `lpdwProcessId` is generated as `uint*` (it
   is nullable/optional), so the code passes `&pid` inside an `unsafe`
   block. If CsWin32 instead emits `out uint`, change the call to
   `PInvoke.GetWindowThreadProcessId(hwnd, out pid)`.
3. `GetWindowText` / `GetClassName` take `PWSTR`; the code passes
   `(PWSTR)buf` from `stackalloc char[]`.
4. `EnumWindows` takes an `ENUM_WINDOW_PROC` delegate; a lambda returning
   `true` (implicitly converted to `BOOL`) is supplied.
5. `SetWinEventHook` takes `WINEVENTPROC` delegate and returns
   `HWINEVENTHOOK`; `UnhookWinEvent` takes that handle. The callback's
   `@event` param is `uint` and `idObject`/`idChild` are `int`.
   `default(HMODULE)` is used for the module handle (out-of-context hook
   needs no DLL).
6. `RegisterHotKey`'s modifiers param is `HOT_KEY_MODIFIERS`; the code casts
   `(HOT_KEY_MODIFIERS)(Modifiers | MOD_NOREPEAT)`.

If any of these differ, the fix is local to the file that uses the API.

## Rule persistence & auto-apply

### Rule store

`RuleEngine` persists to `%APPDATA%\AltTabExcluder\rules.json`. Rules are
keyed by process name (case-insensitive, no extension). Writes are atomic
(temp file + `File.Replace`/`File.Move`) so a crash cannot corrupt the
store. Loading is fault-tolerant: a corrupt or inaccessible file yields an
empty rule set rather than crashing the tray app. A "show" rule
(`Exclude=false`) is not stored — it is removed, since showing is the
default — so every persisted rule has `Exclude=true`.

### Auto-apply on window creation

`WindowEventWatcher` installs a `SetWinEventHook(EVENT_OBJECT_CREATE)` hook
with `WINEVENT_OUTOFCONTEXT | WINEVENT_SKIPOWNPROCESS`. Callbacks arrive on
the WinForms UI thread via the message loop — no DLL injection. Each event
is validated (`IsWindow` + `IsWindowVisible` + has a process name) before a
rule is applied. Because a brand-new window may not yet be visible or titled
when `EVENT_OBJECT_CREATE` fires, the watcher re-checks once after a short
delay (300 ms one-shot `WinForms.Timer`) if the immediate check fails.

### Tray menu integration

- **Quick Exclude** — lists all open visible windows with a checkmark
  reflecting current `WS_EX_TOOLWINDOW` state. Clicking toggles the style
  live. Windows that naturally carry `WS_EX_TOOLWINDOW` (tool palettes,
  helper windows) are shown with a checkmark but greyed out — they weren't
  excluded by AltTabExcluder, so toggling them is disabled.
- **Always Exclude** — lists currently running processes (deduplicated) with
  a checkmark reflecting whether a persistent rule exists. Also lists saved
  rules for processes that aren't currently running, under a separator.
  Clicking toggles the rule and applies it to all currently open windows of
  that process.

### Run at Windows startup

`StartupManager` writes/removes a value in
`HKCU\Software\Microsoft\Windows\CurrentVersion\Run` (per-user, no admin
required, matching the `asInvoker` manifest). The tray menu's "Run at
Windows startup" checkbox reflects and toggles this. The command value is
quoted to survive paths with spaces.

## Elevation policy

The manifest requests `asInvoker` (not `requireAdministrator`) so the app
does not trigger a UAC prompt on every launch. UIPI will block interaction
with elevated target windows; `ElevationDetector` detects this case (via
`OpenProcess` → `OpenProcessToken` → `GetTokenInformation(TokenElevation)`)
and the hotkey handler shows a warning toast directing the user to the tray
**Restart as Administrator** action (re-launches the current exe with
`Verb = "runas"`) rather than silently failing.

The global hotkey works on every window regardless of frame type
(Electron/Chromium/UWP/custom-frame) because `WS_EX_TOOLWINDOW` applies to
any `HWND` — there is no system-menu injection or custom-frame detection in
this codebase.

## Clean teardown

`TrayIconManager`, `HotkeyManager`, and `WindowEventWatcher` all implement
`IDisposable`. `Program.ShutdownApp` saves settings (persisting the
`ExcludedByUs` HWND set), then disposes services in order: unhook WinEvent
→ unregister hotkey → hide tray icon → `Application.Exit()`. Rules persist
across restarts by design (they are the user's saved preferences), so
windows are intentionally not auto-restored on exit — the next launch
re-applies them via `WindowEventWatcher`, and the Quick Exclude menu lets
the user re-show any window at any time.
