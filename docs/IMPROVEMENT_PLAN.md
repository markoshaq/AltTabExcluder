# AltTabExcluder Improvement Plan

This document describes all identified weak points and the concrete work
required to address each one. It is intended to be picked up by a future
session and executed item-by-item.

Each item has: **Problem**, **Goal**, **Approach**, **Files touched**, and
**Acceptance criteria**.

---

## 1. Simplify the coverage gate story

### Problem

The CI enforces two coverage gates:

1. An **overall floor of 10%** — so low it's barely a guardrail. It exists
   only because Win32/UI/entry-point code can't be unit-tested without a
   live desktop session, dragging the total down.
2. A **per-file gate of 70%** on five testable logic files
   (`AppSettings.cs`, `RuleEngine.cs`, `ExclusionTracker.cs`,
   `ProcessRule.cs`, `AppLogger.cs`). `WindowManager.cs` is excluded from
   this gate because its Win32 methods drag the file-level number below 70%,
   even though its pure style-bit math is fully tested.

This two-gate dance is confusing to read in CI output and undermines the
"we enforce coverage" message. The 10% floor looks like a joke; the
exclusion of `WindowManager` looks like gaming the metric.

### Goal

Make the coverage strategy honest and easy to understand. The real quality
signal is: "every testable logic layer is ≥70% covered." Own that number
directly.

### Approach

- **Remove the overall 10% floor.** It catches nothing meaningful — a
  total regression would also show up as a per-file drop. Replace it with
  a non-gating informational print of the overall number (so it's visible
  in CI logs but doesn't fail the build).
- **Add `WindowManager.cs` to the per-file gate but split the coverage
  measurement.** The file has two kinds of code:
  - Pure style-bit math (`ComputeExcludedStyle`, `ComputeVisibleStyle`,
    `IsStyleExcluded`) — fully testable, should be ≥70%.
  - Win32 interop methods (`GetExtendedStyle`, `SetExtendedStyle`,
    `ToggleStyle`, `SetStyle`) — untestable without a desktop session.
  
  Two options (pick one):
  
  **Option A (preferred): Extract the pure math into a separate file.**
  Move `ComputeExcludedStyle`, `ComputeVisibleStyle`, `IsStyleExcluded`
  into a new `WindowStyleMath.cs` (static class). `WindowManager` calls
  into it. Add `WindowStyleMath.cs` to the per-file gate. `WindowManager.cs`
  stays excluded (it's now purely Win32 interop). This is the cleanest —
  the testable code is physically separated from the untestable code.
  
  **Option B: Use cobertura method-level filtering.** Configure coverlet
  to exclude specific methods via attributes (`[ExcludeFromCodeCoverage]`
  on the Win32 interop methods) and include `WindowManager.cs` in the
  per-file gate. Less clean — attributes on methods are noise and the
  exclusion is coarse.
  
- **Update the CI `Parse coverage & enforce gates` step** to reflect the
  new single-gate strategy. Update the `::notice::` message and the
  per-file table header.
- **Update the `Update live coverage badge` step** to compute the min
  across the updated testable file list (including `WindowStyleMath.cs`
  if Option A).
- **Update AGENTS.md** "Coverage gates" section and README "Coverage
  strategy" section to describe the simplified approach.

### Files touched

- `.github/workflows/ci.yml` — coverage parsing, badge step
- `src/AltTabExcluder/WindowManager.cs` — extract math (Option A)
- `src/AltTabExcluder/WindowStyleMath.cs` — new file (Option A)
- `tests/AltTabExcluder.Tests/WindowManagerStyleMathTests.cs` — update
  references if extraction changes the API surface
- `AGENTS.md` — coverage gates section
- `README.md` — coverage strategy section

### Acceptance criteria

- CI has a single enforced gate: per-file ≥70% on all testable logic
  files (including the extracted style math).
- The overall coverage number is printed for information but does not
  fail the build.
- The coverage badge reflects the min per-file rate across the updated
  file list.
- All existing tests still pass.
- AGENTS.md and README accurately describe the new strategy.

---

## 2. Add integration/E2E tests for the Win32 layer

### Problem

The core functionality — toggling `WS_EX_TOOLWINDOW` / `WS_EX_APPWINDOW`
on real window handles, hotkey registration, WinEvent hooks — has zero
automated test coverage. "Manual testing" is the only safety net. This is
the riskiest part of the app and the most likely to regress.

### Goal

Add a set of integration tests that exercise the Win32 interop layer
against real (test-created) windows on a live desktop session. These
tests should run in CI on `windows-latest` (which has a desktop session
available via the runner's interactive session).

### Approach

Create a new test project or a new folder in the existing test project:
`tests/AltTabExcluder.Tests/Integration/`.

**Key challenge:** These tests need a live desktop session. The GitHub
Actions `windows-latest` runner does have one (it runs in an interactive
session), but locally a developer needs to run them on a desktop session
(not in a headless CI context). Mark them with a custom trait
`[Trait("Category", "Integration")]` so they can be filtered.

**Test cases to implement:**

1. **`WindowManager_IntegrationTests`**
   - Create a test window (WinForms `Form` shown non-modally), get its
     HWND, call `SetStyle` to exclude it, assert `IsStyleExcluded` returns
     true and `GetExtendedStyle` has `WS_EX_TOOLWINDOW` set and
     `WS_EX_APPWINDOW` cleared.
   - Call `SetStyle` to restore, assert the inverse.
   - Call `ToggleStyle` twice, assert it round-trips.
   - Assert that `SetWindowPos(SWP_FRAMECHANGED)` is actually called
     (verify by checking the window's style is immediately readable as
     changed — no delay).

2. **`HotkeyManager_IntegrationTests`**
   - Register a hotkey with a unique combo (e.g. `Ctrl+Alt+Shift+F12`
     to avoid collisions), assert `RegisterHotKey` succeeded.
   - Simulate the hotkey press (this is tricky — see notes below) and
     assert the event fires.
   - Unregister and assert no event fires on the same key combo.
   - Test that registering a hotkey already taken by another app fails
     gracefully.
   
   **Note on simulating hotkey presses:** `RegisterHotKey` relies on the
   OS hotkey table. Simulating a press via `SendInput` or `keybd_event`
   may not trigger the `WM_HOTKEY` message reliably. Alternative: use a
   `NativeWindow` to listen for `WM_HOTKEY` and call the handler
   directly, or use `SendMessage` to post `WM_HOTKEY` to the
   message-only window. The test may need to be designed as "register,
   post WM_HOTKEY manually, assert handler invoked" rather than "press
   physical keys."

3. **`WindowEventWatcher_IntegrationTests`**
   - Create a `WindowEventWatcher` with a rule for a test process name.
   - Create a new `Form` with a title matching the rule.
   - Assert the watcher's callback fires and the window gets the
     excluded style applied.
   - Create a child window (e.g. a `Panel` with a handle) and assert
     the watcher does NOT apply the rule to it (child filtering via
     `GetAncestor(GA_ROOT)`).

4. **`WindowEnumerationService_IntegrationTests`**
   - Create several test windows with known titles.
   - Call `Enumerate()` and assert the test windows appear in the list
     with correct titles and process names.
   - Assert filtered windows (desktop, taskbar) do NOT appear.
   - Assert the app's own windows do NOT appear.

5. **`ExclusionService_IntegrationTests`**
   - Create a test window, call `ExclusionService.Toggle`, assert
     `IsExcluded` returns true and `WasExcludedByUs` returns true.
   - Call `RestoreAll`, assert the window is no longer excluded and
     `WasExcludedByUs` returns false.
   - Call `PruneStale` after closing the test window, assert the HWND
     is removed from the tracker.

**CI integration:**

- In `.github/workflows/ci.yml`, add a step after the unit test step:
  ```
  dotnet test --filter "Category=Integration" --no-build -c Debug
  ```
- These tests only run on `windows-latest` (which they already do).
- If a developer runs `dotnet test` locally on a headless session, the
  integration tests should be skipped or fail gracefully with a clear
  message ("requires a desktop session"). Use a `Skip` attribute or a
  runtime check (`System.Windows.Forms.Application.OpenForms` or
  similar) to detect headless mode.

### Files touched

- `tests/AltTabExcluder.Tests/Integration/` — new folder with test files
- `tests/AltTabExcluder.Tests/AltTabExcluder.Tests.csproj` — may need
  references to `System.Windows.Forms` (already has them via the main
  project reference)
- `.github/workflows/ci.yml` — add integration test step
- `AGENTS.md` — document the integration test category
- `README.md` — mention integration tests in the testing section

### Acceptance criteria

- At least 5 integration test files covering: WindowManager style
  toggling, HotkeyManager registration/firing, WindowEventWatcher
  rule application + child filtering, WindowEnumerationService
  enumeration + filtering, ExclusionService toggle/restore/prune.
- Integration tests pass in CI on `windows-latest`.
- Integration tests are tagged with `[Trait("Category", "Integration")]`
  and can be filtered out of local headless runs.
- Unit tests (`dotnet test --filter "Category!=Integration"`) still
  pass and are unaffected.

---

## 3. Harden HWND recycling mitigation

### Problem

`ExclusionTracker` persists raw HWND values. After a window closes, the
OS can recycle that HWND value for a new, unrelated window. If the
tracker still has the stale HWND, `WasExcludedByUs` could return true
for a window that was never excluded by AltTabExcluder.

Current mitigation: `PruneStale` is called when the tray menu opens,
removing HWNDs that are no longer valid (`IsWindow` returns false). But:

1. If the user never opens the tray menu, stale entries persist
   indefinitely in `settings.json`.
2. There's a race: HWND A closes, HWND B is created with the same value,
   user opens menu — `IsWindow(B)` returns true, so `PruneStale` keeps
   it, but B was never excluded by us. `WasExcludedByUs(B)` returns
   true incorrectly.
3. `RestoreAll` would restore a window that was never excluded.

### Goal

Make the HWND tracking robust enough that stale/recycled HWNDs cannot
cause incorrect behavior.

### Approach

**Layer 1: Validate HWND identity, not just validity.**

An HWND value alone is not a unique identity. To distinguish "the window
we excluded" from "a different window that reused the same HWND," store
additional identity metadata alongside the HWND:

- **Process ID (PID):** Store the PID with each excluded HWND. On
  `WasExcludedByUs`, check that the HWND's current PID matches the
  stored PID. If the PID differs, the HWND was recycled — treat as
  stale.
  
  This is not perfect (PIDs are also recycled), but it catches the
  common case.

- **Window class name + title hash (optional, stronger):** Store a
  hash of the window's class name and title at exclusion time. On
  lookup, compare. If they differ, the HWND was recycled. This is
  more expensive (a `GetClassName` + `GetWindowText` call) so make it
  optional or only use it for `RestoreAll`.

  **Recommended:** Start with PID. It's cheap (`GetWindowThreadProcessId`
  is already used elsewhere) and catches the vast majority of cases.

**Layer 2: Proactive pruning, not just on menu open.**

- Add a periodic prune via a `WinForms.Timer` (e.g. every 60 seconds)
  that calls `PruneStale`. This ensures stale entries are cleaned up
  even if the user never opens the menu.
- Also prune on `RestoreAll` (before restoring, so we don't try to
  restore recycled HWNDs).
- Also prune on app startup (in `Program.cs` after loading settings).

**Layer 3: Make `RestoreAll` identity-aware.**

Before restoring an HWND, call `WasExcludedByUs` with the new
identity check (PID match). If the HWND's current PID doesn't match
the stored PID, skip it (it's recycled) and remove it from the tracker.

**Implementation details:**

- Change `ExclusionTracker` to store `(HWND, uint pid)` tuples instead
  of just `HWND`. Update `Add`, `Remove`, `Contains`, `WasExcludedByUs`
  to take/compare PID.
- Update `AppSettings` serialization to persist the PID alongside the
  HWND. Use a JSON array of `{ "hwnd": "0x1234", "pid": 5678 }` objects
  instead of a flat array of HWND strings. Handle the old format on load
  (if the value is a string, treat PID as unknown/0 — backward compat).
- Update `ExclusionService.WasExcludedByUs` to call
  `GetWindowThreadProcessId(hwnd)` and compare the PID.
- Update `ExclusionService.RestoreAll` to prune before restoring and
  skip recycled HWNDs.
- Add the periodic prune timer in `Program.cs` or
  `TrayEventCoordinator`.

### Files touched

- `src/AltTabExcluder/ExclusionTracker.cs` — store (HWND, PID) tuples
- `src/AltTabExcluder/ExclusionService.cs` — identity-aware
  `WasExcludedByUs`, `RestoreAll`, prune calls
- `src/AltTabExcluder/AppSettings.cs` — serialization format change +
  backward compat
- `src/AltTabExcluder/Program.cs` — startup prune, periodic prune timer
- `src/AltTabExcluder/Tray/TrayEventCoordinator.cs` — may own the
  periodic prune timer
- `tests/AltTabExcluder.Tests/ExclusionTrackerTests.cs` — update for
  PID storage
- `tests/AltTabExcluder.Tests/AppSettingsPersistenceTests.cs` — update
  for new serialization format + test backward compat with old format
- `AGENTS.md` — update ExclusionTracker and AppSettings docs
- `README.md` — update the HWND recycling limitation note

### Acceptance criteria

- `ExclusionTracker` stores (HWND, PID) pairs.
- `WasExcludedByUs` returns false for a recycled HWND (different PID).
- `RestoreAll` skips recycled HWNDs and removes them from the tracker.
- `PruneStale` runs on startup, on menu open, on `RestoreAll`, and
  periodically (every 60s).
- Old `settings.json` format (flat HWND array) loads without error;
  PID is treated as unknown until the next save.
- All unit tests pass; new tests cover the PID mismatch case.
- README limitation note updated to describe the mitigation.

---

## 4. Improve the release zip (checksum, README, version info)

### Problem

The release zip contains only `AltTabExcluder.exe`. No checksum, no
in-zip README, no version metadata beyond the tag name. This is
"downloadable" but not "distributable" — users can't verify integrity
and get no quick-start guide inside the archive.

### Goal

Produce a release zip that is self-contained, verifiable, and
user-friendly.

### Approach

In the `release` job's `Zip publish output` step (or a new step after
it):

1. **Add a SHA256 checksum file.**
   ```pwsh
   $exePath = $exe.FullName
   $hash = (Get-FileHash $exePath -Algorithm SHA256).Hash
   "$hash  AltTabExcluder.exe" | Out-File -Encoding ascii "checksums.sha256"
   ```
   Include `checksums.sha256` in the zip alongside the exe.

2. **Add a README.txt inside the zip.**
   Write a short plain-text file with:
   - App name and version (from `$env:GITHUB_REF_NAME`)
   - One-line description
   - "Just run AltTabExcluder.exe — no installation needed."
   - System requirements (Windows 10/11 x64)
   - Link to the full README on GitHub
   - Hotkey default (Win+Alt+X) and how to change it
   - Data location (`%APPDATA%\AltTabExcluder\`)
   - MIT license notice

3. **Add a LICENSE.txt inside the zip.**
   Copy the repo's `LICENSE` file into the zip as `LICENSE.txt`.

4. **Update the zip step to include all three files:**
   ```pwsh
   Compress-Archive -Path $exe.FullName, "checksums.sha256", "README.txt", "LICENSE.txt" `
     -DestinationPath "AltTabExcluder-$env:GITHUB_REF_NAME.zip"
   ```

5. **Upload the checksum file as a separate release asset too** (some
   users want to verify without opening the zip):
   ```pwsh
   gh release create $tag "AltTabExcluder-$tag.zip" "checksums.sha256" ...
   ```

### Files touched

- `.github/workflows/ci.yml` — release job zip step + release create step

### Acceptance criteria

- The release zip contains: `AltTabExcluder.exe`,
  `checksums.sha256`, `README.txt`, `LICENSE.txt`.
- `checksums.sha256` contains the SHA256 hash of the exe in standard
  `coreutils` format (`<hash>  <filename>`).
- The checksum file is also uploaded as a standalone release asset.
- `README.txt` is plain-text, human-readable, includes the version and
  quick-start instructions.
- `LICENSE.txt` is the MIT license text.

---

## 5. Add a local `dotnet format` pre-commit hook

### Problem

`dotnet format` is enforced in CI but not locally. Developers can push
format drift that only gets caught in CI, creating a slow feedback loop.

### Goal

Provide an easy way to run `dotnet format` locally before committing,
so format drift is caught before push.

### Approach

**Do NOT use a git pre-commit hook by default** — they're fragile on
Windows, require manual setup, and can slow down commits. Instead:

1. **Add a `dotnet format` script** to the repo:
   `scripts/format.ps1` that runs:
   ```pwsh
   dotnet format AltTabExcluder.sln
   ```
   And a `scripts/format-check.ps1` that runs:
   ```pwsh
   dotnet format AltTabExcluder.sln --verify-no-changes
   ```

2. **Document it in CONTRIBUTING.md** and AGENTS.md:
   - "Run `scripts/format.ps1` before committing to auto-fix style."
   - "CI runs `scripts/format-check.ps1` — run it locally to avoid
     CI failures."

3. **Optional: provide a pre-commit hook setup script** for developers
   who want it. Create `scripts/install-hooks.ps1` that copies a
   `pre-commit` script into `.git/hooks/`. The `pre-commit` script runs
   `dotnet format --verify-no-changes` and fails the commit if drift is
   found. This is opt-in — the developer runs
   `scripts/install-hooks.ps1` once to enable it.

   The `pre-commit` hook content:
   ```sh
   #!/bin/sh
   dotnet format AltTabExcluder.sln --verify-no-changes --no-restore
   ```
   (Git hooks on Windows can run shell scripts via Git Bash.)

4. **Add the scripts to the repo** and reference them in docs.

### Files touched

- `scripts/format.ps1` — new
- `scripts/format-check.ps1` — new
- `scripts/install-hooks.ps1` — new (opt-in hook installer)
- `scripts/pre-commit` — new (the hook script itself, copied by the
  installer)
- `CONTRIBUTING.md` — document the format workflow
- `AGENTS.md` — mention the scripts in the build & run section

### Acceptance criteria

- `scripts/format.ps1` runs `dotnet format` and auto-fixes style.
- `scripts/format-check.ps1` runs `dotnet format --verify-no-changes`
  and exits non-zero on drift.
- `scripts/install-hooks.ps1` installs a working `pre-commit` hook.
- CONTRIBUTING.md and AGENTS.md document the workflow.
- Running `scripts/format-check.ps1` on a clean tree exits 0.

---

## 6. Tighten the README

### Problem

The README is ~400 lines and reads like internal documentation. It's
thorough but ineffective as a landing page — visitors see a wall of text
before they understand what the app does and why they should care.

### Goal

Restructure the README so the top section is a concise, compelling
overview (what it is, what it does, screenshot, install link). Move
detailed content into linked sub-documents.

### Approach

**New README structure:**

1. **Hero section (above the fold):**
   - App name + icon + one-line tagline.
   - 2-3 sentence description (what it does, who it's for).
   - Badges (CI, license, .NET, coverage).
   - One screenshot (the tray icon + hotkey demo GIF if available, or
     the Quick Exclude submenu).

2. **Quick start (immediately after hero):**
   - Download link to Releases.
   - "Run AltTabExcluder.exe. Press Win+Alt+X to hide the focused
     window from Alt+Tab."
   - Build from source (3 commands).

3. **Features (concise bullet list, no sub-paragraphs):**
   - Keep the current feature list but trim each to one line. No
     em-dashes with long explanations. Just the feature name and a
     few words.

4. **Links to detailed docs:**
   - "📖 [Full usage guide](docs/USAGE.md)"
   - "🔧 [How it works](docs/HOW_IT_WORKS.md)"
   - "🏗️ [Architecture](docs/ARCHITECTURE.md)"
   - "🧪 [Testing & coverage](docs/TESTING.md)"
   - "📋 [Changelog](CHANGELOG.md)"
   - "🤝 [Contributing](CONTRIBUTING.md)"

5. **Limitations (keep — it's important for trust):**
   - Keep the current 4 bullet points, trimmed.

6. **License (one line).**

**New sub-documents to create:**

- `docs/USAGE.md` — the full usage section (hide focused window, tray
  menu, always exclude, restore all, change hotkey, enable/disable,
  elevated windows). Move the current "Usage" section here verbatim.
- `docs/HOW_IT_WORKS.md` — the current "How It Works" section (style
  toggling, auto-apply mechanism, storage table, tech stack). Move
  verbatim.
- `docs/TESTING.md` — the current "Development" + "Coverage strategy"
  sections. Move verbatim.

**Keep in README:** hero, quick start, features (trimmed), limitations,
license, links to sub-docs.

**Target length:** ~100-120 lines for the new README (down from ~400).

### Files touched

- `README.md` — rewrite (shorter)
- `docs/USAGE.md` — new (content moved from README)
- `docs/HOW_IT_WORKS.md` — new (content moved from README)
- `docs/TESTING.md` — new (content moved from README)

### Acceptance criteria

- README is under ~120 lines.
- A visitor can understand what the app does and how to install it
  without scrolling past the first screen.
- All detailed content is preserved in the sub-documents — no
  information is lost.
- All internal links work (sub-docs link to each other where
  relevant).
- The GitHub repo landing page shows the hero section + quick start
  + features without requiring "Readme" navigation.

---

## Execution order

Recommended order of implementation (by dependency and impact):

1. **Item 1 (coverage gates)** — foundational, affects CI and test
   structure. Do first so subsequent test work fits the new structure.
2. **Item 2 (integration tests)** — highest risk-reduction. Depends on
   Item 1's test structure being clean.
3. **Item 3 (HWND recycling)** — correctness fix. Independent of 1-2
   but touches the same test files.
4. **Item 4 (release zip)** — quick win, CI-only change.
5. **Item 5 (format scripts)** — quick win, tooling only.
6. **Item 6 (README)** — pure docs, do last so it reflects all the
   above changes.

Items 4, 5, and 6 are independent and can be done in parallel or in
any order. Items 1-3 should be done sequentially.
