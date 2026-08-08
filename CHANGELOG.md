# Changelog

All notable changes to this project are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added
- `.editorconfig` defining the canonical C# formatting & naming style,
  enforced via `dotnet format` in CI.
- `AnalysisLevel` set to `latest` with `EnforceCodeStyleInBuild` so style
  regressions surface at build time.
- `dotnet format --verify-no-changes` step in CI to block formatting drift.
- GitHub Release automation: pushing a `v*` tag builds the single-file exe
  and publishes it as a downloadable release asset.
- Coverage-strategy notice in CI output explaining the two-gate design.
- `CONTRIBUTING.md`, `docs/ARCHITECTURE.md`, PR template, and this changelog.
- Screenshots section in the README with image embeds ready to fill.

### Changed
- `RuleEngine._rules` is now `readonly` (analyzer-recommended).
- Test `Dispose` methods call `GC.SuppressFinalize` (CA1816).
- `WindowManagerTests.cs` line endings normalized to CRLF.

## [0.1.0] - 2025-01-01

### Added
- Tray-only Windows utility to exclude windows from the Alt+Tab switcher.
- Hotkey toggle (default `Win+Alt+X`, customizable via a picker dialog).
- Quick Exclude — live per-window toggles from the tray menu.
- Always Exclude — persistent per-process rules auto-applied to new windows
  via a `SetWinEventHook` watcher.
- Restore All — one-click un-exclude of every window AltTabExcluder hid.
- Single-instance guard via a named `Mutex`.
- Run-at-startup toggle (per-user, no admin required).
- Elevation-aware: detects elevated target windows and offers restart-as-admin.
- Atomic (temp + move) persistence for settings, rules, and a rotating log.
- 102 unit tests covering the pure logic layers, with dual coverage gates in CI.
- Single-file self-contained publish profile.
- GitHub Actions CI: Debug + Release builds, tests with coverage, single-file
  publish.

[Unreleased]: https://github.com/markoshaq/AltTabExcluder/compare/v0.1.0...HEAD
[0.1.0]: https://github.com/markoshaq/AltTabExcluder/releases/tag/v0.1.0
