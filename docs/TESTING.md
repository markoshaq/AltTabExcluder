# Testing & Coverage

## Running tests

```powershell
# Unit tests (pure logic layers — no desktop session needed)
dotnet test --filter "Category!=Integration"

# Integration tests (require a live Windows desktop session)
dotnet test --filter "Category=Integration"

# All tests
dotnet test
```

## Unit tests

The test project (`tests/AltTabExcluder.Tests/`) covers the pure logic
layers: `WindowStyleMath` style-bit math, `RuleEngine` persistence,
`AppSettings` formatting and round-trip, `ExclusionTracker` (including
PID-aware identity checks), `AppLogger`, and `ProcessRule` record semantics.

## Integration tests

Integration tests (in `tests/AltTabExcluder.Tests/Integration/`) exercise
the Win32 interop layer against real (test-created) windows on a live
desktop session. They are tagged with `[Trait("Category", "Integration")]`
and cover:

- **`WindowManagerIntegrationTests`** — style toggling on real windows.
- **`HotkeyManagerIntegrationTests`** — hotkey registration, conflict
  detection, and WM_HOTKEY dispatch.
- **`WindowEventWatcherIntegrationTests`** — rule auto-application to new
  top-level windows + child window filtering.
- **`WindowEnumerationServiceIntegrationTests`** — enumeration + filtering.
- **`ExclusionServiceIntegrationTests`** — toggle/restore/prune orchestration.

## Coverage strategy

A single gate is enforced in CI: each testable logic layer (`WindowStyleMath`,
`AppSettings`, `RuleEngine`, `ExclusionTracker`, `ProcessRule`, `AppLogger`)
must have &ge;70% line coverage. The overall coverage number is printed for
information but does not fail the build — it is low because
Win32/UI/entry-point code requires a live desktop session.

`WindowManager`'s pure style-bit math was extracted into `WindowStyleMath`
so it can be gated independently of the untestable Win32 interop methods
that remain in `WindowManager`.

## CI

CI runs on every push/PR via GitHub Actions (`.github/workflows/ci.yml`):
`dotnet format` style check, Debug build, unit tests with coverage, integration
tests, Release build, single-file publish. Pushing a `v*` tag triggers a
release job that creates a GitHub Release with the zipped single-file exe,
checksum, README, and LICENSE.
