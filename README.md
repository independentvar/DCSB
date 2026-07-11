# Deathcounter and Soundboard (DCSB)

A free, open source soundboard and death counter for Windows, made for gamers and streamers. Bind sounds and counters to global hotkeys (keyboard or mouse buttons) that work even while your game has focus, route the soundboard together with your microphone into Discord or your game through a virtual cable, and keep count of anything — deaths, wins, curse words — with the number written to a text file your streaming software can display.

![Application screenshot](screenshot.png?raw=true "Main Window")

## Highlights

- **Global hotkeys** — trigger sounds and counters with keyboard shortcuts or mouse buttons from inside any game; the app doesn't need focus.
- **Plays into Discord and games** — mixes your sounds with your microphone into a second output device (a virtual cable such as VB-CABLE), so people in voice chat hear both your voice and the soundboard. A built-in **setup wizard** walks you through the virtual cable, audio routing, and verifying that Discord will hear you.
- **Noise suppression** — clean up your microphone with a fast mode (RNNoise, ~10 ms latency) or a high-quality mode (DeepFilterNet3, noticeably better on keyboard clatter and background voices). Low-latency mic passthrough throughout.
- **Microphone controls** — mic volume with boost above 100%, live level meter, and a mute/unmute hotkey.
- **In-game overlay** — an optional bar over fullscreen games listing your sounds and their hotkeys, with adjustable position, size, and opacity.
- **Death counter for streams** — each counter writes its count (with a custom text format) to a text file that OBS or other streaming software can show on stream.
- **Wide format support** — mp3, wav, ogg (Vorbis and Opus), opus, m4a, aac, mp4, wma, aiff, and flac, with volume normalization across sounds so nothing blows your ears out.
- **Presets** — switch between whole sets of sounds and counters for different games, even with a hotkey.
- **No nonsense** — free, open source, no ads, no accounts. One-click updates from GitHub releases; new installers are scanned with VirusTotal and include GitHub build-provenance attestations.

## How to install

1. Go to the [latest release page](https://github.com/independentvar/DCSB/releases/latest) and download the installer — the file named like `DCSB_4.1.0.0.exe` (under **Assets**).
2. Run the downloaded file. If Windows shows a blue **"Windows protected your PC"** screen, click **More info** and then **Run anyway** — this appears because the installer isn't digitally signed, not because anything is wrong.
3. If Windows asks **"Do you want to allow this app to make changes to your device?"**, click **Yes**.
4. Follow the installer: click **Next**, pick an install folder (the suggested one is fine), and click **Install**.
5. When it finishes, leave **Start Deathcounter and Soundboard** checked and click **Finish** — the app starts right away. Later you can find it in the Start Menu under **Deathcounter and Soundboard**.

To update, just install a newer version the same way — it replaces the old one (the app also offers one-click updates when a new version is out). To uninstall, use **Add or remove programs** in Windows Settings.

DCSB needs the **.NET 10 Desktop Runtime (x64)**; if it's missing, the installer points you to the download page.

To verify that a newly released installer was built by this repository's GitHub Actions workflow, install the [GitHub CLI](https://cli.github.com/) and run:

```powershell
gh attestation verify .\DCSB_4.1.0.0.exe --repo independentvar/DCSB
```

Replace the example filename with the installer you downloaded.

## Using it in Discord or a game

1. Install a virtual audio cable (for example [VB-CABLE](https://vb-audio.com/Cable/)).
2. In DCSB **Settings → Sounds**, set the **second output device** to the cable's playback device (e.g. *CABLE Input*) and pick your real microphone under **Microphone**.
3. In Discord (or your game), select the cable's recording device (e.g. *CABLE Output*) as your microphone.
4. Done — voice chat now hears your voice and your sounds mixed together. Don't enable Windows' "Listen to this device" on the cable.

Or just run **Settings → Other → Run setup wizard**, which walks you through all of this and verifies the result.

## Sounds

For each sound you can set:

- **Name** and **key or key combination** (keyboard or mouse) to play it; configure repeated presses to restart/layer, stop, or pause/resume each sound
- **One or more sound files** — a random one plays each time, great for variety
- **Volume** per sound and whether to **loop**

Plus, in the sound list and settings:

- Drag and drop audio files straight onto the window to add them
- Automatically assign keys (number row or numpad) to new sounds
- Reorder rows, see each sound's duration, and scrub playback with a seekbar
- Overlap sounds or play one at a time; pause, continue, and stop-all hotkeys
- Two output devices with separate volume and mute — e.g. your headphones *and* the virtual cable
- Normalize volume across sounds

## Counters

For each counter you can set:

- **Name** — to identify it
- **Path to a text file** — the current count is written there so your streaming software can display it
- **Count** and **increment** — the number added or subtracted
- **Format** — custom text around the number (e.g. `Deaths: {0}`)

Hotkeys are available for next/previous counter, increment, decrement, and reset. Counter files can also be dragged onto the window to add them.

## Presets

Create presets to switch between whole lists of counters and sounds for different games or situations — instantly, even with a keyboard shortcut.

## Other features

- In-game overlay showing your sounds and hotkeys over fullscreen games (use borderless fullscreen; true exclusive fullscreen can hide it)
- Automatic in-game overlay warning when a fullscreen elevated game can prevent a non-administrator DCSB from receiving global hotkeys, with one-click restart as administrator
- Backup and restore all settings and key bindings to a file
- Minimize to tray, single-instance, enable/disable or show/hide the counter and sound panels
- Automatic update check with one-click install of new versions

## Bugs and feedback

If something breaks — a crash, a sound that won't play, a hotkey that stops working — please [open an issue](https://github.com/independentvar/DCSB/issues). Include as much as you can: error messages, what you were doing, and the version you're on. Restarting the app (or running it as administrator) often helps in the meantime.
