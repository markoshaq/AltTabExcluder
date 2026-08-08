# Contributing to AltTabExcluder

Thanks for your interest in contributing! This is a small, focused project, so
the conventions below are lightweight.

## Prerequisites

- .NET 8 SDK
- Windows 10/11 (x64) — the app uses WinForms and Win32 interop
- Git

## Getting started

```powershell
git clone https://github.com/markoshaq/AltTabExcluder.git
cd AltTabExcluder
dotnet restore
dotnet build -c Debug
dotnet test
```

All tests should pass before you start.

## Workflow

1. **Open an issue first** for anything beyond a trivial fix — it avoids
   duplicated work and lets us align on approach.
2. Fork the repo and create a branch from `main`:
   `git checkout -b feature/short-description`.
3. Make your change. Keep commits focused; write clear commit messages.
4. Run the full verification suite locally (see below).
5. Open a pull request targeting `main` and fill in the PR template.

## Verification — run all of these before opening a PR

```powershell
# Build (analyzers run at build time; warnings must be clean)
dotnet build -c Debug

# Tests
dotnet test

# Formatting — must report no changes
dotnet format AltTabExcluder.sln --verify-no-changes --no-restore
```

CI runs these same checks plus a Release build and coverage gates, so a green
local run almost always means a green CI run.

## Code style

Style is defined in [`.editorconfig`](.editorconfig) and enforced two ways:

- At build time (`EnforceCodeStyleInBuild=true`, `AnalysisLevel=latest`).
- In CI via `dotnet format --verify-no-changes`.

If `dotnet format` wants to change your code, run `dotnet format` (without
`--verify-no-changes`) to apply the fixes, then commit. Don't fight the
formatter.

Key conventions already in use:

- File-scoped namespaces.
- `var` when the type is apparent; explicit types otherwise.
- `sealed` classes by default.
- Private fields are `_camelCase`.
- XML doc comments on public APIs.
- No `TODO`/`FIXME` left in committed code — open an issue instead.

## Testing

The testable layers (pure logic, no Win32/UI) are unit-tested in
`tests/AltTabExcluder.Tests/`. Win32/UI/entry-point code is **not** unit-tested
because it requires a live desktop session — it's covered by manual testing.

CI enforces a **per-file gate of 70%** on the testable logic layers
(`AppSettings`, `RuleEngine`, `ExclusionTracker`, `ProcessRule`, `AppLogger`).
If you change one of these files, make sure its coverage stays above the gate.

When adding a new testable logic class, add it to the `$testableFiles` list in
[`.github/workflows/ci.yml`](.github/workflows/ci.yml) so it's covered by the
per-file gate.

## Architecture

See [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md) for the full design,
Win32 interop notes, and storage layout.

## Commits and releases

- Keep commits focused and write messages that explain *why*, not just *what*.
- This project follows [Semantic Versioning](https://semver.org/). Releases are
  tagged `vX.Y.Z`; pushing a tag triggers CI to build and publish a GitHub
  Release with the single-file exe.
- Update [`CHANGELOG.md`](CHANGELOG.md) under the `[Unreleased]` section for
  user-facing changes.

## Reporting bugs

Use the [bug report template](.github/ISSUE_TEMPLATE/bug_report.md). Include
the contents of `%APPDATA%\AltTabExcluder\app.log` if it exists.
