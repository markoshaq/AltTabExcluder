# AltTabExcluder

Lightweight Windows system-tray utility (.NET 8, WinForms) that excludes
specific running windows from the Alt+Tab switcher by toggling the Win32
`WS_EX_TOOLWINDOW` / `WS_EX_APPWINDOW` extended styles on target window
handles. The app is **tray-only**: no main window, no dashboard. All user
interaction happens through the tray context menu and a configurable global
hotkey.

## Build & run

> Requires the .NET 8 SDK and Windows 10/11 (x64).

```powershell
dotnet restore
dotnet build -c Debug
dotnet test --filter "Category!=Integration"  # unit tests
dotnet test --filter "Category=Integration"   # integration tests (desktop session)
scripts\format.ps1                             # auto-fix style
scripts\format-check.ps1                       # verify style (CI-equivalent)
dotnet run --project src\AltTabExcluder
```

Target framework is `net8.0-windows`, x64 only. WinForms only
(`UseWindowsForms=true`). `AllowUnsafeBlocks` for CsWin32 pointer parameters.
DPI awareness is `PerMonitorV2` (via `ApplicationHighDpiMode` project property).
The app icon (`assets/app.ico`) is embedded as a resource so it is available
at runtime in all scenarios including single-file publish.

### Single-file publish

```powershell
dotnet publish -c Release -p:PublishProfile=SingleFile
```

Produces a self-contained `AltTabExcluder.exe` (~68 MB) at
`bin\x64\Release\net8.0-windows\win-x64\publish\` — no .NET installation
required on the target machine. Trimming is not enabled (WinForms uses
reflection; the SDK blocks it with `NETSDK1175`).

### CI

GitHub Actions workflow at `.github/workflows/ci.yml` runs on every push/PR
to `main` (and on `v*` tags): runs a `dotnet format` style check, builds
(Debug + Release), runs tests with coverlet coverage, and enforces a
per-file coverage gate (see Testing below). The Release job also publishes the
single-file exe. Pushing a `v*` tag triggers a `release` job that creates a
GitHub Release with the zipped single-file exe attached.

Style is defined in `.editorconfig` and enforced at build time
(`AnalysisLevel=latest`, `EnforceCodeStyleInBuild=true`) plus the CI
`dotnet format --verify-no-changes` step.

## Architecture

### Entry point

- `Program.cs` — WinForms entry point. Acquires a named `Mutex`
  (`Global\AltTabExcluder_SingleInstance`) and exits if another instance is
  running. Wires up all services, creates a `TrayEventCoordinator` to handle
  events, and manages lifecycle (startup sweep + clean teardown). The
  `finally` block in `Main` releases the single-instance `Mutex`.

### Core logic

- `WindowStyleMath.cs` — **stateless** static class with pure style-bit math
  (`ComputeExcludedStyle`, `ComputeVisibleStyle`, `IsStyleExcluded`) and the
  `WS_EX_TOOLWINDOW` / `WS_EX_APPWINDOW` constants. No Win32 calls — fully
  unit-testable.
- `WindowManager.cs` — **stateless** static class with Win32 style read/write.
  `GetExtendedStyle` / `SetExtendedStyle` read/write via `GetWindowLongPtr` /
  `SetWindowLongPtr` with a `SetWindowPos(SWP_FRAMECHANGED)` broadcast so the
  shell immediately re-evaluates taskbar/Alt+Tab presence. `ToggleStyle` /
  `SetStyle` delegate the style computation to `WindowStyleMath` and apply the
  change but do **not** track which HWNDs were excluded — that's the caller's
  job (`ExclusionService`).
- `ExclusionTracker.cs` — instance-based tracking set for HWNDs AltTabExcluder
  excluded (vs. natural tool windows). Stores `(HWND, PID)` pairs so recycled
  HWNDs (same value, different process) can be detected via `WasExcludedByUs`.
  Persisted via `AppSettings` so Quick Exclude can identify our exclusions
  after restart. `PruneStale` removes invalid HWNDs. Backward-compatible with
  the old flat-array settings format (PID=0 means "unknown").
- `ExclusionService.cs` — the single entry point for all exclusion operations.
  Orchestrates `WindowManager` (style) + `ExclusionTracker` (tracking):
  `Toggle`, `SetExcluded`, `IsExcluded`, `WasExcludedByUs` (PID-aware),
  `RestoreAll` (skips recycled HWNDs + clears tracker), `PruneStale`. Owned
  by `Program`, injected into `RuleEngine`, `TrayIconManager`, and
  `WindowEnumerationService`.

### Services (`Services/`)

- `WindowEnumerationService.cs` — `EnumWindows` snapshot of open visible
  top-level windows, enriched with process name + icon. Filters out desktop,
  taskbar, IME, tray overflow, `TextInputHost`, and the app's own windows.
  Icons cached by EXE path for the app lifetime. Takes an optional
  `ExclusionService` to populate exclusion state.
- `WindowInfo.cs` — immutable record: HWND, PID, process name, title, icon,
  exclusion state.
- `HotkeyManager.cs` — registers a configurable global hotkey (default
  `Win+Alt+X`) via `RegisterHotKey` on a hidden message-only `NativeWindow`
  (`HWND_MESSAGE`). `IDisposable`.
- `RuleEngine.cs` — JSON persistence to `%APPDATA%\AltTabExcluder\rules.json`
  (atomic temp+move write). Rules keyed by process name (case-insensitive).
  `ApplyTo` delegates the style change to an optional callback so the
  persistence layer is decoupled from Win32 and testable in isolation.
  Internal constructor accepts a custom file path for testing.
- `ProcessRule.cs` — record: `ProcessName`, `Exclude`, `CreatedAt`.
- `WindowEventWatcher.cs` — `SetWinEventHook(EVENT_OBJECT_CREATE |
  EVENT_OBJECT_SHOW)` with `WINEVENT_OUTOFCONTEXT | WINEVENT_SKIPOWNPROCESS`;
  auto-applies matching rules to newly created **top-level** windows (child
  windows filtered via raw `GetAncestor(GA_ROOT)` P/Invoke). One-shot
  `WinForms.Timer` re-checks windows not yet visible/titled when the event
  fires. `IDisposable`.
- `AppSettings.cs` — persists to `%APPDATA%\AltTabExcluder\settings.json`.
  Holds hotkey config, enabled state, and the excluded-by-us HWND list.
  `FormatHotkey` / `KeyToString` for human-readable labels. Internal
  `Load(string)` / `Save(string)` overloads for testing.
- `AppLogger.cs` — lightweight file logger to
  `%APPDATA%\AltTabExcluder\app.log` with 256 KB size-based rotation.
  Thread-safe. All `Debug.WriteLine` and silent `catch` blocks route here.
  Internal `SetLogDirectory` for test redirection.
- `Win32Constants.cs` — named constants for MOD_* / VK_* values. Replaces
  scattered magic numbers.
- `StartupManager.cs` — `HKCU\...\Run` toggle for "Run at Windows startup"
  (per-user, no admin needed).
- `ElevationDetector.cs` — token-based elevation check via
  `NativeElevationInterop` (raw P/Invoke). Cached for the process lifetime.
  Intentionally does not log (called on every WinEvent callback).
- `NativeElevationInterop.cs` — raw `DllImport` for `OpenProcess` /
  `OpenProcessToken` / `GetTokenInformation`. Avoids CsWin32 to sidestep
  cumbersome `SafeHandle` / `TOKEN_ACCESS_MASK` types.

### Tray (`Tray/`)

- `TrayIconManager.cs` — owns the `NotifyIcon` and context menu: Quick
  Exclude (live per-window toggles), Always Exclude (persistent per-process
  rules), Restore All, Settings (hotkey toggle, change hotkey, startup,
  restart-as-admin), About, Exit. Submenus repopulated on every open.
  Excludes submenus stay open after a click for multi-toggle. Takes
  `RuleEngine` + `ExclusionService`. `IDisposable`.
- `TrayEventCoordinator.cs` — handles all tray/hotkey event logic: hotkey
  press, hotkey toggle/change, restore-all, startup toggle, about dialog,
  restart-as-admin, shutdown. Extracted from `Program` to keep the entry
  point focused on wiring and lifecycle. Takes all services + teardown
  callbacks (for mutex release + exit).

### UI (`UI/`)

- `HotkeyPickerDialog.cs` — WinForms `Form` capturing a keyboard shortcut via
  a low-level `WH_KEYBOARD_LL` hook (raw P/Invoke to capture Win-key combos).
  Requires at least one modifier.
- `AboutDialog.cs` — modal `Form` with app info, how-it-works, data location,
  current hotkey, and clickable GitHub link.

## Win32 interop

CsWin32 (v0.3.298) generates bindings from `NativeMethods.txt` into
`Windows.Win32.PInvoke`. Consuming code uses the generated `HWND` / `LPARAM`
/ `PWSTR` / `HWINEVENTHOOK` types. APIs in `NativeMethods.txt`:
`GetForegroundWindow`, `GetWindowLongPtr`, `SetWindowLongPtr`, `SetWindowPos`,
`IsWindow`, `IsWindowVisible`, `RegisterHotKey`, `UnregisterHotKey`,
`EnumWindows`, `GetWindowText`, `GetWindowTextLength`, `GetClassName`,
`GetWindowThreadProcessId`, `SetWinEventHook`, `UnhookWinEvent`.

Three APIs use raw `DllImport` instead of CsWin32 (by design — see file docs):
`GetAncestor` (`WindowEventWatcher`), `OpenProcess`/`OpenProcessToken`/
`GetTokenInformation` (`NativeElevationInterop`), `SetWindowsHookEx`/
`UnhookWindowsHookEx`/`CallNextHookEx`/`GetModuleHandle`
(`HotkeyPickerDialog`).

## Storage

| File | Contents |
|------|----------|
| `%APPDATA%\AltTabExcluder\settings.json` | Hotkey config, enabled state, excluded-by-us HWND+PID list |
| `%APPDATA%\AltTabExcluder\rules.json` | Persistent per-process exclusion rules |
| `%APPDATA%\AltTabExcluder\app.log` | Rotating log file (256 KB → `app.log.bak`) |

All files are written atomically (temp + move) and loaded fault-tolerantly
(corrupt file → defaults, not a crash).

## Testing

Test project: `tests/AltTabExcluder.Tests/` (xUnit, `net8.0-windows`).

```powershell
dotnet test
```

Tests covering the testable (non-Win32-UI) layers:

- **`WindowManagerStyleMathTests`** — pure style-bit math in
  `WindowStyleMath.cs`: bit manipulation, other-bit preservation, idempotency,
  round-trip correctness.
- **`AppSettingsFormatHotkeyTests`** — `FormatHotkey` / `KeyToString`: default
  combo, NoRepeat stripping, canonical modifier order, all key-code mappings.
- **`AppSettingsPersistenceTests`** — load/save round-trip, defaults,
  corrupt-file recovery, directory creation.
- **`RuleEngineTests`** — persistence, case-insensitivity, exclude=false
  removal, upsert timestamps, cross-instance persistence, corrupt-file
  recovery, `ApplyTo` callback.
- **`ExclusionTrackerTests`** — add/remove/contains, load/get-for-save
  round-trip, clear, deduplication.
- **`AppLoggerTests`** — log writing, severity filtering, exception logging,
  directory creation, log redirection.
- **`ProcessRuleTests`** — record equality, property values, hash codes.

The main project exposes `InternalsVisibleTo("AltTabExcluder.Tests")` for
internal constructors and overloads that redirect file I/O to temp paths.

### Integration tests

Integration tests (in `tests/AltTabExcluder.Tests/Integration/`) exercise the
Win32 interop layer against real (test-created) windows on a live desktop
session. They are tagged with `[Trait("Category", "Integration")]` and cover:

- **`WindowManagerIntegrationTests`** — style toggling on real windows.
- **`HotkeyManagerIntegrationTests`** — hotkey registration, conflict
  detection, and WM_HOTKEY dispatch.
- **`WindowEventWatcherIntegrationTests`** — rule auto-application to new
  top-level windows + child window filtering.
- **`WindowEnumerationServiceIntegrationTests`** — enumeration + filtering.
- **`ExclusionServiceIntegrationTests`** — toggle/restore/prune orchestration.

Run integration tests separately:
```powershell
dotnet test --filter "Category=Integration"
```

CI runs them in a separate step after the unit test / coverage run. To run
only unit tests locally (e.g. in a headless context):
```powershell
dotnet test --filter "Category!=Integration"
```

### Coverage gates

CI enforces a single coverage gate (via coverlet + cobertura XML parsing):

- **Per-file gate (70%)** on the testable logic layers: `WindowStyleMath.cs`,
  `AppSettings.cs`, `RuleEngine.cs`, `ExclusionTracker.cs`, `ProcessRule.cs`,
  `AppLogger.cs`. Every testable logic layer must be ≥70% line-covered.

The overall coverage number is printed for information but does not fail the
build — it is low because Win32/UI/entry-point code requires a live desktop
session. `WindowManager.cs` is excluded from coverage measurement because its
pure style-bit math was extracted into `WindowStyleMath.cs` (which IS gated);
the remaining Win32 interop methods are untestable without a desktop session.

Win32/UI/entry-point files are excluded from coverage measurement because
they require a live Windows desktop session.
