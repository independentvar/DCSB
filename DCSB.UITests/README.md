# DCSB UI / integration tests

PowerShell scripts that drive the real, built `DCSB.exe` through UI Automation
and verify behavior at the user-visible surface — including that playing a
sound produces an actual signal on the selected output device, measured via
the endpoint's WASAPI peak meter.

These are **not** run by CI and are not part of `DCSB.sln`. They require:

- **PowerShell 7+ (`pwsh`)** — the tests load the app's `net10.0` assemblies,
  which Windows PowerShell 5.1 (.NET Framework) cannot. Running under 5.1 fails
  at startup with a `ReflectionTypeLoadException`. Install with
  `winget install Microsoft.PowerShell` if needed.
- an **interactive Windows desktop session** (they move the mouse, click, and
  bring windows to the foreground — don't run one while typing),
- at least one **active audio output device**,
- a **Release build**: `dotnet build DCSB.sln -c Release`.

## Running

Run from a PowerShell 7 session (`pwsh`), not the default Windows PowerShell:

```powershell
cd DCSB.UITests
pwsh -File .\Run-UITests.ps1                    # all tests
pwsh -File .\Run-UITests.ps1 -Filter Playback   # subset by file-name regex
pwsh -File .\Test-PlaybackProducesAudio.ps1     # or run a single test directly
```

The exit code of `Run-UITests.ps1` is the number of failed tests. Individual
tests exit 0 (pass), 1 (fail) or 2 (skip — preconditions not met on this
machine).

## What is covered

| Test | Verifies |
|---|---|
| `Test-LegacyDeviceNameMigration` | Device names truncated to 31 chars by the old WaveOut enumeration (configs written by ≤ 4.5.x) are upgraded to full MMDevice friendly names in `config.xml` on startup. Skipped if no device name on the machine exceeds 31 chars. |
| `Test-PlaybackProducesAudio` | Double-clicking a sound produces measurable signal on the selected device; interrupt-and-replay and replay-after-finish both restart the shared `WasapiOut` correctly. |
| `Test-CounterIncrement` | Selecting a counter and pressing the main-window Increment/Decrement buttons changes its count and writes the value — through the counter's `Format` — to its `.txt` file. Covers the configured increment step, negative counts, and the count surviving an app restart (read back from the file). |
| `Test-PresetSwitch` | With two presets configured, the main window lists only the active preset's sounds; choosing another preset from the `Preset: <name>` menu swaps the visible list and persists `SelectedPresetIndex` to `config.xml`. Switching back confirms it's a live toggle. |
| `Test-DeviceDropdownFullNames` | Settings → Sound dropdowns list `Disabled`, `Default Output Device`, and every active render endpoint with its full untruncated name. |
| `Test-LiveDeviceSwitch` | Switching the first output device at runtime (disposes and recreates the playback engine) doesn't crash, and playback works after switching back. |
| `Test-AddSoundNamePrefill` | Selecting file/s in the Sound dialog prefills an empty Name with the first file's name (without extension); a later selection doesn't overwrite an already-set name. |
| `Test-SoundDialogSingleClick` | A single left-click on the Sound dialog's read-only File/s box opens the file picker, and a single click on the Key/s box opens the key-binding window (both previously needed a double-click). The Key/s box must open on button *release* (like the `...` button), so the opening click isn't captured by the global mouse hook as a "Left Click" binding. |
| `Test-CounterDialogSingleClick` | A single left-click on the Counter dialog's read-only File box opens the file picker (previously needed a double-click). |
| `Test-AddCounterNamePrefill` | Same prefill behavior for the Counter dialog's file picker. |
| `Test-AutoAssignKeys` | New sounds get the first free auto-assigned key (`KEY_1` by default); switching Settings → Shortcuts to the numpad key set makes the next sound get `NUMPAD1`; unchecking the feature stops assignment. Verified in the saved `config.xml`. |
| `Test-SingleInstance` | Launching a second `DCSB.exe` exits it (single-instance mutex) and restores the minimized window of the already-running instance. |

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
- For the common single-sound setup call `New-DcsbTestConfig`. For anything
  richer (extra presets, counters), compose a config with `New-DcsbConfig`,
  `New-DcsbPreset`, `New-DcsbSound`, `New-DcsbCounter` and write it with
  `Save-DcsbConfigModel`. `New-DcsbCounter` needs the target file to already
  exist (a counter's count is read from its file, not the config).
- Throw a string starting with `SKIP:` when the machine can't support the test.
- The Settings window is modal: while it is open, clicks on the main window
  are silently ignored. Call `Close-DcsbSettings` before playing sounds.
- The test sound is a `.wav` from `%windir%\Media` (prefers `Alarm01.wav`),
  so tests don't depend on any user files.
