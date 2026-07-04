# DCSB UI / integration tests

PowerShell scripts that drive the real, built `DCSB.exe` through UI Automation
and verify behavior at the user-visible surface — including that playing a
sound produces an actual signal on the selected output device, measured via
the endpoint's WASAPI peak meter.

These are **not** run by CI and are not part of `DCSB.sln`. They require:

- an **interactive Windows desktop session** (they move the mouse, click, and
  bring windows to the foreground — don't run one while typing),
- at least one **active audio output device**,
- a **Release build**: `msbuild DCSB.sln /p:Configuration=Release`.

## Running

```powershell
cd DCSB.UITests
.\Run-UITests.ps1                 # all tests
.\Run-UITests.ps1 -Filter Playback   # subset by file-name regex
.\Test-PlaybackProducesAudio.ps1     # or run a single test directly
```

The exit code of `Run-UITests.ps1` is the number of failed tests. Individual
tests exit 0 (pass), 1 (fail) or 2 (skip — preconditions not met on this
machine).

## What is covered

| Test | Verifies |
|---|---|
| `Test-LegacyDeviceNameMigration` | Device names truncated to 31 chars by the old WaveOut enumeration (configs written by ≤ 4.5.x) are upgraded to full MMDevice friendly names in `config.xml` on startup. Skipped if no device name on the machine exceeds 31 chars. |
| `Test-PlaybackProducesAudio` | Double-clicking a sound produces measurable signal on the selected device; interrupt-and-replay and replay-after-finish both restart the shared `WasapiOut` correctly. |
| `Test-DeviceDropdownFullNames` | Settings → Sound dropdowns list `Disabled`, `Default Output Device`, and every active render endpoint with its full untruncated name. |
| `Test-LiveDeviceSwitch` | Switching the first output device at runtime (disposes and recreates the playback engine) doesn't crash, and playback works after switching back. |

## Safety around user state

DCSB stores its config machine-wide at `%ProgramData%\DCSB\config.xml`. Every
test backs it up, replaces it with a generated test config (built by
serializing the app's own `DCSB.Models` types, so it always matches the
current schema), and restores the original afterwards — even on failure. The
runner keeps an additional outer backup and restarts any DCSB instance (e.g.
the installed release) that was running before the tests started.

## Notes for writing new tests

- Dot-source `UITestHelpers.ps1` and wrap the body in `Invoke-UITest` — it
  handles config backup/restore, app teardown, and PASS/FAIL/SKIP reporting.
- Throw a string starting with `SKIP:` when the machine can't support the test.
- The Settings window is modal: while it is open, clicks on the main window
  are silently ignored. Call `Close-DcsbSettings` before playing sounds.
- The test sound is a `.wav` from `%windir%\Media` (prefers `Alarm01.wav`),
  so tests don't depend on any user files.
