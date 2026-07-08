# End-to-end test of the in-app microphone capture feature. DCSB's microphone
# is set to VB-Cable's "CABLE Output"; a 440 Hz tone rendered into "CABLE
# Input" acts as the user's voice. DCSB must mix that voice into its secondary
# output, which is metered via its WASAPI peak meter. Covers: voice reaching
# the output, the mic surviving sound playback/interrupt/device switches, the
# settings combo + live level meter + volume slider, disable, persistence, and
# restoring from config on restart. Skipped when VB-Cable is not installed.
. "$PSScriptRoot\UITestHelpers.ps1"

function New-ToneWav {
    # 0.6 s 880 Hz mono PCM16 wav - a short soundboard sound so natural end-of-sound
    # (which removes mixer inputs) happens quickly during the test
    param([string]$Path)
    $rate = 44100; $seconds = 0.6; $freq = 880
    $count = [int]($rate * $seconds)
    $data = New-Object byte[] ($count * 2)
    for ($i = 0; $i -lt $count; $i++) {
        $sample = [int16]([Math]::Sin(2 * [Math]::PI * $freq * $i / $rate) * 0.6 * 32767)
        [BitConverter]::GetBytes($sample).CopyTo($data, $i * 2)
    }
    $ms = New-Object IO.MemoryStream
    $bw = New-Object IO.BinaryWriter($ms)
    $bw.Write([Text.Encoding]::ASCII.GetBytes('RIFF')); $bw.Write([int32](36 + $data.Length))
    $bw.Write([Text.Encoding]::ASCII.GetBytes('WAVEfmt ')); $bw.Write([int32]16)
    $bw.Write([int16]1); $bw.Write([int16]1); $bw.Write([int32]$rate); $bw.Write([int32]($rate * 2)); $bw.Write([int16]2); $bw.Write([int16]16)
    $bw.Write([Text.Encoding]::ASCII.GetBytes('data')); $bw.Write([int32]$data.Length)
    $bw.Write($data)
    [IO.File]::WriteAllBytes($Path, $ms.ToArray())
    $bw.Dispose()
}

$script:ToneOut = $null

function Start-Voice {
    # renders a continuous 440 Hz "voice" into CABLE Input, which VB-Cable loops
    # to the CABLE Output capture endpoint that DCSB records from
    param([Parameter(Mandatory)] $Device)
    Stop-Voice
    $gen = New-Object NAudio.Wave.SampleProviders.SignalGenerator(48000, 2)
    $gen.Frequency = 440
    $gen.Gain = 0.5
    $wave = New-Object NAudio.Wave.SampleProviders.SampleToWaveProvider($gen)
    $script:ToneOut = New-Object NAudio.Wave.WasapiOut($Device, [NAudio.CoreAudioApi.AudioClientShareMode]::Shared, $true, 50)
    $script:ToneOut.Init($wave)
    $script:ToneOut.Play()
}

function Stop-Voice {
    if ($script:ToneOut) {
        $script:ToneOut.Stop()
        $script:ToneOut.Dispose()
        $script:ToneOut = $null
    }
}

Invoke-UITest -Name 'MicrophoneMixedIntoSecondaryOutput' -Body {
    $cableInName = 'CABLE Input (VB-Audio Virtual Cable)'
    $cableOutName = 'CABLE Output (VB-Audio Virtual Cable)'

    $enumerator = New-Object NAudio.CoreAudioApi.MMDeviceEnumerator
    $render = $enumerator.EnumerateAudioEndPoints([NAudio.CoreAudioApi.DataFlow]::Render, [NAudio.CoreAudioApi.DeviceState]::Active)
    $cableIn = $render | Where-Object { $_.FriendlyName -eq $cableInName } | Select-Object -First 1
    if (-not $cableIn) { throw 'SKIP: VB-Cable not installed.' }

    # pick a non-cable secondary output that is currently silent (no ambient
    # audio to pollute the baseline) and whose peak meter demonstrably registers
    # (never a cable device: secondary->cable->mic would be a feedback loop)
    $secondaryName = $null
    foreach ($candidate in ($render | Where-Object { $_.FriendlyName -notlike '*VB-Audio*' })) {
        try {
            $ambient = Get-MaxPeak -DeviceName $candidate.FriendlyName -Seconds 1
            if ($ambient -gt 0.02) { Write-Host "skipping $($candidate.FriendlyName): ambient audio (peak $ambient)"; continue }
            Start-Voice -Device $candidate
            $peak = Get-MaxPeak -DeviceName $candidate.FriendlyName -Seconds 1
            Stop-Voice
            if ($peak -gt 0.05) { $secondaryName = $candidate.FriendlyName; break }
            Write-Host "skipping $($candidate.FriendlyName): meter does not register (peak $peak)"
        } catch { Stop-Voice }
    }
    if (-not $secondaryName) { throw 'SKIP: no quiet, meterable non-cable render endpoint.' }
    Write-Host "Secondary output for the test: $secondaryName"

    $wav = Join-Path ([IO.Path]::GetTempPath()) 'dcsb-mic-verify.wav'
    New-ToneWav -Path $wav

    $config = New-DcsbConfig -PrimaryOutput 'Disabled'
    $config.SecondaryOutput = $secondaryName
    $config.SecondaryDeviceVolume = 100
    $config.MicrophoneInput = $cableOutName
    $config.MicrophoneVolume = 100
    $preset = New-DcsbPreset -Name 'UITest'
    $preset.SoundCollection.Add((New-DcsbSound -Name $script:TestSoundName -File $wav))
    $config.PresetCollection.Add($preset)
    Save-DcsbConfigModel $config

    try {
        $process = Start-Dcsb
        $main = Get-DcsbMainWindow -ProcessId $process.Id
        Start-Sleep -Seconds 2

        # 1. baseline: no voice, no sound -> silence on the secondary output
        $baseline = Get-MaxPeak -DeviceName $secondaryName -Seconds 2
        Write-Host ("1. baseline peak (mic attached, silence): {0:F4}" -f $baseline)
        Assert-True ($baseline -lt 0.02) "expected near-silence at baseline, got $baseline"

        # 2. voice only: tone into CABLE Input must come out of DCSB's secondary output
        Start-Voice -Device $cableIn
        Start-Sleep -Milliseconds 500
        $voicePeak = Get-MaxPeak -DeviceName $secondaryName -Seconds 2
        Write-Host ("2. voice-only peak: {0:F4}" -f $voicePeak)
        Assert-True ($voicePeak -gt 0.1) "voice did not reach the secondary output (peak $voicePeak)"

        # 3. soundboard sound while voice flows (Overlap=false: PlaySound runs the
        # internal Stop() which must not remove the mic leg); after the short sound
        # ends naturally the voice must still be there
        Invoke-PlayTestSound $main
        Start-Sleep -Seconds 2
        $afterSound = Get-MaxPeak -DeviceName $secondaryName -Seconds 2
        Write-Host ("3. voice peak after sound played and ended: {0:F4}" -f $afterSound)
        Assert-True ($afterSound -gt 0.1) "voice lost after playing a sound (peak $afterSound)"

        # 4. interrupt path: two rapid plays (second one stops the first mid-play)
        Invoke-PlayTestSound $main
        Invoke-PlayTestSound $main
        Start-Sleep -Seconds 2
        $afterInterrupt = Get-MaxPeak -DeviceName $secondaryName -Seconds 2
        Write-Host ("4. voice peak after interrupt-and-replay: {0:F4}" -f $afterInterrupt)
        Assert-True ($afterInterrupt -gt 0.1) "voice lost after interrupt-and-replay (peak $afterInterrupt)"

        # 5. settings UI: third combo is the microphone input with the expected items
        $settings = Open-DcsbSettings $main
        Select-DcsbSettingsTab $settings 'Sound'
        $comboCondition = New-Object System.Windows.Automation.PropertyCondition(
            [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
            [System.Windows.Automation.ControlType]::ComboBox)
        $combos = $settings.FindAll([System.Windows.Automation.TreeScope]::Descendants, $comboCondition)
        Assert-True ($combos.Count -eq 3) "expected 3 combos on the Sound tab, found $($combos.Count)"
        $micCombo = $combos[2]
        Assert-True ($micCombo.GetCurrentPattern([System.Windows.Automation.SelectionPattern]::Pattern).Current.GetSelection()[0].Current.Name -eq $cableOutName) 'mic combo does not show the configured device'
        $items = Get-ComboItemNames $micCombo
        Write-Host ("5. mic combo items: {0}" -f ($items -join ' | '))
        Assert-True ($items -contains 'Disabled') 'mic combo missing Disabled'
        Assert-True ($items -contains 'Default Input Device') 'mic combo missing Default Input Device'
        Assert-True ($items -contains $cableOutName) 'mic combo missing the capture device'

        # 6. live input level meter shows the voice
        $barCondition = New-Object System.Windows.Automation.PropertyCondition(
            [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
            [System.Windows.Automation.ControlType]::ProgressBar)
        $meter = $settings.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $barCondition)
        Assert-True ($null -ne $meter) 'level meter progress bar not found'
        $level = $meter.GetCurrentPattern([System.Windows.Automation.RangeValuePattern]::Pattern).Current.Value
        Write-Host ("6. level meter while voice plays: {0:F4}" -f $level)
        Assert-True ($level -gt 0.1) "level meter shows no input (value $level)"

        # 7. microphone volume slider is live: 30% must be quieter than 100%
        $sliderCondition = New-Object System.Windows.Automation.PropertyCondition(
            [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
            [System.Windows.Automation.ControlType]::Slider)
        $sliders = $settings.FindAll([System.Windows.Automation.TreeScope]::Descendants, $sliderCondition)
        $micSlider = $sliders[$sliders.Count - 1]  # last slider on the tab
        $micSlider.GetCurrentPattern([System.Windows.Automation.RangeValuePattern]::Pattern).SetValue(30)
        Start-Sleep -Milliseconds 800
        $quiet = Get-MaxPeak -DeviceName $secondaryName -Seconds 2
        Write-Host ("7. voice peak at 30% mic volume: {0:F4} (was {1:F4} at 100%)" -f $quiet, $voicePeak)
        Assert-True ($quiet -gt 0.02 -and $quiet -lt ($voicePeak * 0.6)) "30% gain not quieter than 100% ($quiet vs $voicePeak)"
        $micSlider.GetCurrentPattern([System.Windows.Automation.RangeValuePattern]::Pattern).SetValue(100)

        # 8. switching the secondary output away and back must re-attach the mic
        $secondCombo = $combos[1]
        Select-ComboItem $secondCombo 'Disabled'
        Start-Sleep -Milliseconds 800
        Select-ComboItem $secondCombo $secondaryName
        Start-Sleep -Milliseconds 800
        $afterSwitch = Get-MaxPeak -DeviceName $secondaryName -Seconds 2
        Write-Host ("8. voice peak after secondary switched Disabled->back: {0:F4}" -f $afterSwitch)
        Assert-True ($afterSwitch -gt 0.1) "mic not re-attached after device switch (peak $afterSwitch)"

        # 9. disabling the microphone stops the voice and persists
        Select-ComboItem $micCombo 'Disabled'
        Start-Sleep -Milliseconds 800
        $disabled = Get-MaxPeak -DeviceName $secondaryName -Seconds 2
        Write-Host ("9. voice peak with mic Disabled: {0:F4}" -f $disabled)
        Assert-True ($disabled -lt 0.02) "voice still audible with mic disabled (peak $disabled)"
        Close-DcsbSettings $settings
        Start-Sleep -Seconds 2  # config save debounce
        $configText = Get-Content $script:ConfigPath -Raw
        Assert-True ($configText -match '<MicrophoneInput>Disabled</MicrophoneInput>') 'config.xml does not persist MicrophoneInput=Disabled'

        # 9b. sounds branch isolated: mic disabled (voice detached), the soundboard
        # sound alone must still come out of the reworked secondary engine
        Invoke-PlayTestSound $main
        $soundOnly = Get-MaxPeak -DeviceName $secondaryName -Seconds 1.5
        Write-Host ("9b. sound-only peak with mic disabled: {0:F4}" -f $soundOnly)
        Assert-True ($soundOnly -gt 0.1) "soundboard sound not audible on secondary output (peak $soundOnly)"

        # 10. re-enable via UI, restart the app: mic must come back from config alone
        $settings = Open-DcsbSettings $main
        Select-DcsbSettingsTab $settings 'Sound'
        $combos = $settings.FindAll([System.Windows.Automation.TreeScope]::Descendants, $comboCondition)
        Select-ComboItem $combos[2] $cableOutName
        Close-DcsbSettings $settings
        Start-Sleep -Seconds 2
        Stop-Dcsb $process
        Start-Sleep -Seconds 1
        $process = Start-Dcsb
        Start-Sleep -Seconds 2
        $restarted = Get-MaxPeak -DeviceName $secondaryName -Seconds 2
        Write-Host ("10. voice peak after app restart: {0:F4}" -f $restarted)
        Assert-True ($restarted -gt 0.1) "mic not restored from config after restart (peak $restarted)"

        Stop-Dcsb $process
    } finally {
        Stop-Voice
        Remove-Item $wav -Force -ErrorAction SilentlyContinue
    }
}
