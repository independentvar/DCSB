# Deathcounter and Soundboard

Deathcounter and Soundboard allows you to create keyboard shortcuts to trigger sound effects and keep count of something (for example deaths in game). Key shortcuts are recognized even if application is not focused.

![Application screenshot](screenshot.png?raw=true "Main Window")

## How to install
1. Go to the [latest release page](https://github.com/independentvar/DCSB/releases/latest) and download the installer — the file named like `DCSB_4.1.0.0.exe` (under **Assets**).
2. Run the downloaded file. If Windows shows a blue **"Windows protected your PC"** screen, click **More info** and then **Run anyway** — this appears because the installer isn't digitally signed, not because anything is wrong.
3. If Windows asks **"Do you want to allow this app to make changes to your device?"**, click **Yes**.
4. Follow the installer: click **Next**, pick an install folder (the suggested one is fine), and click **Install**.
5. When it finishes, leave **Start Deathcounter and Soundboard** checked and click **Finish** — the app starts right away. Later you can find it in the Start Menu under **Deathcounter and Soundboard**.

To update, just install a newer version the same way — it replaces the old one. To uninstall, use **Add or remove programs** in Windows Settings.

## For each counter you can set:
- *Name* - to easily identify counters
- *Path to text file* - file where current count is stored and can be used to display it on stream
- *Count*
- *Increment* - number that is added/subtracted
- *Format* - allows you to add custom text to accompany count number

## For each sound you can set:
-	*Name* - to easily identify different sounds
-	*Path to one or more sound files*, random one will be played any time you hit specified key. Supported file formats are: wma, mp3, wav, ogg, m4a, aiff, and flac
-	*Key or key combination* - to play this sound
-	*Volume* - for this sound
-	*Loop* - whether or not to loop this sound

## Presets
You can create Presets to quickly switch between lists of counters and sounds for different situations, even with keyboard shortcut.

## Application allows you to set some more keyboard shortcut:
-	Select next counter
-	Select previous counter
-	Increment counter
-	Decrement counter
-	Reset counter
-	Pause all playing sounds
-	Continue playing sound
-	Stop all playing sounds

## Additional settings:
-	Overlap sounds
-	Select one or two specific sound output devices
-	Enable/disable counters or sounds
-	Display/hide counters or sounds
-	Minimize to tray

## Using DCSB with Discord or other voice apps (virtual audio cables)
To play sounds into a voice chat, set one of DCSB's output devices to a virtual cable (e.g. **CABLE Input** from [VB-Audio Virtual Cable](https://vb-audio.com/Cable/)) and select the cable's other end (**CABLE Output**) as the microphone in your voice app. If the sound comes out choppy, scratchy or "pixelated" on the other side:

- **Match the cable's sample rates.** In Windows Sound settings (*Sound Control Panel → Properties → Advanced*), set both the **CABLE Input** playback device and the **CABLE Output** recording device to the same format, ideally **48000 Hz, 2 channels**. Mismatched rates between the two ends are the most common cause of crackling.
- **Turn off voice processing in the voice app.** Discord's noise suppression, echo cancellation and automatic gain control treat music and sound effects as noise and will gate or distort them. Disable them for the cable input, or use Push to Talk while testing.

## Please read:
If you come across any bug, something stops working or the program crashes please create issue here. Include as much information as you can provide (any error messages, what stopped working, what were you doing when it happened, what version you are using...).
Usually restarting the program/running it as an administrator helps.
