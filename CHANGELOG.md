# Changelog

All notable changes to this project are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

No unreleased changes.

## [0.1.0] - 2026-08-08

### Added
- Tray-only Windows utility to exclude windows from the Alt+Tab switcher by
  toggling `WS_EX_TOOLWINDOW` / `WS_EX_APPWINDOW` extended window styles.
- Hotkey toggle (default `Win+Alt+X`, customizable via a picker dialog with
  low-level `WH_KEYBOARD_LL` hook to capture Win-key combos).
- Quick Exclude — live per-window toggles from the tray menu with checkmarks
  and process icons.
- Always Exclude — persistent per-process rules auto-applied to new windows
  via a `SetWinEventHook` watcher (`EVENT_OBJECT_CREATE` / `EVENT_OBJECT_SHOW`).
- Restore All — one-click un-exclude of every window AltTabExcluder hid.
- Custom hotkey picker dialog — any modifier+key combo, at least one modifier
  required.
- Single-instance guard via a named `Mutex` (`Global\AltTabExcluder_SingleInstance`).
- Run-at-startup toggle (per-user via `HKCU\...\Run`, no admin required).
- Elevation-aware: detects elevated target windows and offers restart-as-admin.
- Atomic (temp + move) JSON persistence for settings, rules, and a rotating
  log file (256 KB with `.bak` backup).
- `WindowEnumerationService` — `EnumWindows` snapshot enriched with process
  name and icon, with filtering of system windows and icon caching by EXE path.
- `ExclusionService` / `ExclusionTracker` — orchestrates Win32 style changes
  with HWND tracking to distinguish our exclusions from natural tool windows.
- `AppLogger` — thread-safe file logger with severity filtering and size-based
  rotation.
- `Win32Constants` — named constants for MOD_* / VK_* values.
- 102 xUnit tests covering the pure logic layers: `WindowManager` style-bit
  math, `RuleEngine` persistence, `AppSettings` formatting/round-trip,
  `ExclusionTracker`, `AppLogger`, and `ProcessRule` semantics.
- `InternalsVisibleTo` for test-only constructors and file I/O redirection.
- Single-file self-contained publish profile (`SingleFile.pubxml`).
- GitHub Actions CI: Debug + Release builds, `dotnet format` style check,
  tests with coverlet coverage, single-file publish.
- Dual coverage gates in CI: 10% overall floor (regression guard) and 70%
  per-file gate on testable logic layers.
- GitHub Release automation: pushing a `v*` tag builds the single-file exe
  and publishes it as a downloadable release asset.
- Coverage-strategy notice in CI output explaining the two-gate design.
- `.editorconfig` defining canonical C# formatting and naming style, enforced
  via `dotnet format` in CI.
- `AnalysisLevel=latest` with `EnforceCodeStyleInBuild` so style regressions
  surface at build time.
- `.gitattributes` for explicit EOL and binary file handling.
- `CONTRIBUTING.md`, `docs/ARCHITECTURE.md` with data-flow diagram, PR
  template, and issue templates.
- Custom app icon (SVG source + multi-size transparent ICO: 16/32/48/256).
- Screenshots in README: tray icon, Quick Exclude, Always Exclude, Settings,
  hotkey picker, About dialog, and an animated GIF demo of the hotkey in action.

### Changed
- `RuleEngine._rules` is `readonly` (IDE0044).
- Test `Dispose` methods call `GC.SuppressFinalize` (CA1816).
- `CA1707` scoped off for test files (xUnit's `Method_Scenario_Expected` convention).

[Unreleased]: https://github.com/markoshaq/AltTabExcluder/compare/v0.1.0...HEAD
[0.1.0]: https://github.com/markoshaq/AltTabExcluder/releases/tag/v0.1.0
