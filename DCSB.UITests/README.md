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
| `Test-SoundDialogSingleClick` | A single left-click on the Sound dialog's read-only File/s box opens the file picker, and a single click on the Key/s box starts inline key capture in place. Capture must start on button *release* so the opening click isn't captured; Escape cancels without recording; and a click *inside* the listening box is captured as a "Left Click" mouse binding (without the box re-arming). |
| `Test-CounterDialogSingleClick` | A single left-click on the Counter dialog's read-only File box opens the file picker (previously needed a double-click). |
| `Test-AddCounterNamePrefill` | Same prefill behavior for the Counter dialog's file picker. |
| `Test-AutoAssignKeys` | New sounds get the first free auto-assigned key (`KEY_1` by default); switching Settings → Shortcuts to the numpad key set makes the next sound get `NUMPAD1`; unchecking the feature stops assignment. Verified in the saved `config.xml`. |
| `Test-MicrophoneMix` | The in-app microphone capture: with the mic set to VB-Cable's `CABLE Output` and a tone playing into `CABLE Input` (standing in for the user's voice), the voice is audible on the secondary output; it survives sound playback, interrupt-and-replay and a live secondary-device switch; the settings tab shows the input combo, a live level meter and a working 0–200 volume slider; disabling stops the voice, persists to `config.xml`, and the mic comes back from config alone after an app restart. Skipped if VB-Cable is not installed or no quiet meterable output exists. |
| `Test-MicrophoneMute` | The microphone mute toggle: the `SoundShortcuts.MuteMicrophone` global keybind (F8 in the test) silences the voice on the secondary output and brings it back, the Settings → Sound mute button does the same with its tooltip flipping Mute/Unmute, moving the mic volume slider while muted stays silent, the input level meter rests at zero while muted and comes back on unmute, and `MicrophoneMuted` persists to `config.xml` and survives an app restart. Skipped if VB-Cable is not installed or no quiet meterable output exists. |
| `Test-Wizard` | The first-run setup wizard: with a fresh (`SetupCompleted=false`) config it auto-opens on launch, step 1 detects the virtual cable and prefers plain `CABLE Input` over the `16ch` variant, step 2 auto-configures the cable as the second output (persisted to `config.xml`), step 3 plays the bundled test tone — metered on `CABLE Input` to prove it reaches the cable — and reports a success verdict from its own `CABLE Output` capture, step 4 shows the cable-latency tip (with the "Open VB-Cable Control Panel..." button present exactly when `VBCABLE_ControlPanel.exe` exists on disk; asserted, not clicked - it elevates), and Finish persists `SetupCompleted=true`. The first output is set to `Disabled` during the test so the tone never plays on real speakers. Skipped if VB-Cable is not installed. |
| `Test-VolumeNormalization` | Volume normalization: two sounds carrying the same tone mastered 20 dB apart play at roughly the same level with `NormalizeVolume` enabled (quiet boosted, loud attenuated toward the −16 LUFS target), the Settings → Sound checkbox toggles the feature live and persists to `config.xml`, and with it disabled the raw level difference comes back. |
| `Test-SingleInstance` | Launching a second `DCSB.exe` exits it (single-instance mutex) and restores the minimized window of the already-running instance. |
| `Test-UpdateCheck` | The full "update available" flow, incl. a **real** installer download. Builds a throwaway `v0.0.0.1` copy of the app (so it's out of date against the real release feed), runs it, verifies the "New version _X_ is available" offer names the actual newest GitHub release, clicks **Yes**, and confirms the newest release's installer really downloads to `%TEMP%` (matching size + PE header). The app then tries to launch that (admin) installer, so **a UAC prompt briefly appears and the screen dims** — the test kills the app right after the download to cancel that launch, so nothing is installed. Needs the .NET SDK (to build the copy). Skipped if the GitHub feed is unreachable, the newest release has no `.exe` asset, or `dotnet` is missing. |

## Measurement tools

`Measure-MicLatency.ps1` is a manual tool, not a pass/fail test (the
`Measure-` prefix keeps it out of the runner's `Test-*.ps1` glob). It measures
the microphone passthrough latency: a tone rendered into `CABLE Input` is
timestamped on the cable's own peak meter and again on DCSB's secondary output
endpoint; the difference is the app's mic-path latency, excluding the tone
generator's buffering (but still including VB-Cable's internal hop and meter
granularity — compare runs against each other rather than reading the number
as absolute). Ten trials by default, reports per-trial values and the median.

```powershell
pwsh -File .\Measure-MicLatency.ps1 [-Trials 10]
```

For reference (cable-as-mic → Steam Streaming Speakers): the pre-2026-07
100 ms output buffer measured ~141 ms median; 30 ms WasapiOut ~69 ms; the
IAudioClient3 output + capture (engine-minimum period) ~57 ms. The granted
period is per-device and per-direction — Steam/NVIDIA render endpoints and
all capture endpoints on this machine give 480 frames (10 ms), while
VB-Cable's render side gives 128 frames (2.67 ms) — so the real streamer
topology (output = cable) runs faster than this probe's direction can show,
and hardware microphones with inbox Win10+ drivers may capture faster than
this machine's endpoints do.

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
