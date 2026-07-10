# End-to-end test of the microphone mute feature. DCSB's microphone is set to
# VB-Cable's "CABLE Output"; a 440 Hz tone rendered into "CABLE Input" acts as
# the user's voice on the secondary output. Covers: the global mute keybind
# (SoundShortcuts.MuteMicrophone) toggling the voice off and back on, the
# settings-page mute button (with its tooltip flipping Mute/Unmute), the volume
# slider staying silent while muted, and the muted state persisting to
# config.xml and surviving an app restart. Skipped when VB-Cable is not
# installed or no quiet meterable output exists.
. "$PSScriptRoot\UITestHelpers.ps1"

$script:ToneOut = $null

function Start-Voice {
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

function Send-F8Key {
    [DcsbUiTest.Native]::keybd_event(0x77, 0, 0, [UIntPtr]::Zero)  # VK_F8 down
    Start-Sleep -Milliseconds 50
    [DcsbUiTest.Native]::keybd_event(0x77, 0, 2, [UIntPtr]::Zero)  # KEYEVENTF_KEYUP
}

Invoke-UITest -Name 'MicrophoneMuteKeybindAndButton' -Body {
    $cableInName = 'CABLE Input (VB-Audio Virtual Cable)'
    $cableOutName = 'CABLE Output (VB-Audio Virtual Cable)'

    $enumerator = New-Object NAudio.CoreAudioApi.MMDeviceEnumerator
    $render = $enumerator.EnumerateAudioEndPoints([NAudio.CoreAudioApi.DataFlow]::Render, [NAudio.CoreAudioApi.DeviceState]::Active)
    $cableIn = $render | Where-Object { $_.FriendlyName -eq $cableInName } | Select-Object -First 1
    if (-not $cableIn) { throw 'SKIP: VB-Cable not installed.' }

    # same endpoint selection as Test-MicrophoneMix: quiet, meterable, non-cable
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

    $config = New-DcsbConfig -PrimaryOutput 'Disabled'
    $config.SecondaryOutput = $secondaryName
    $config.SecondaryDeviceVolume = 100
    $config.MicrophoneInput = $cableOutName
    $config.MicrophoneVolume = 100
    $config.SoundShortcuts.MuteMicrophone.Keys.Add([DCSB.Utils.VKey]::F8)
    $preset = New-DcsbPreset -Name 'UITest'
    $preset.SoundCollection.Add((New-DcsbSound -Name $script:TestSoundName))
    $config.PresetCollection.Add($preset)
    Save-DcsbConfigModel $config

    try {
        $process = Start-Dcsb
        $main = Get-DcsbMainWindow -ProcessId $process.Id
        Start-Sleep -Seconds 2

        # 1. baseline: voice flows to the secondary output
        Start-Voice -Device $cableIn
        Start-Sleep -Milliseconds 500
        $voicePeak = Get-MaxPeak -DeviceName $secondaryName -Seconds 2
        Write-Host ("1. voice peak unmuted: {0:F4}" -f $voicePeak)
        Assert-True ($voicePeak -gt 0.1) "voice did not reach the secondary output (peak $voicePeak)"

        # 2. the F8 keybind mutes the voice
        Set-DcsbForeground $main
        Send-F8Key
        Start-Sleep -Milliseconds 800
        $muted = Get-MaxPeak -DeviceName $secondaryName -Seconds 2
        Write-Host ("2. voice peak after F8 (muted): {0:F4}" -f $muted)
        Assert-True ($muted -lt 0.02) "voice still audible after mute keybind (peak $muted)"

        # 3. F8 again unmutes
        Send-F8Key
        Start-Sleep -Milliseconds 800
        $unmuted = Get-MaxPeak -DeviceName $secondaryName -Seconds 2
        Write-Host ("3. voice peak after second F8 (unmuted): {0:F4}" -f $unmuted)
        Assert-True ($unmuted -gt 0.1) "voice did not come back after unmute keybind (peak $unmuted)"

        # 4. the settings-page mute button mutes and its tooltip flips to Unmute
        $settings = Open-DcsbSettings $main
        Select-DcsbSettingsTab $settings 'Sound'
        $muteButton = Find-ButtonByHelpText -Element $settings -HelpText 'Mute microphone'
        Assert-True ($null -ne $muteButton) 'mute microphone button not found on the Sound tab'
        Invoke-UIElement $muteButton
        Start-Sleep -Milliseconds 800
        $mutedByButton = Get-MaxPeak -DeviceName $secondaryName -Seconds 2
        Write-Host ("4. voice peak after mute button: {0:F4}" -f $mutedByButton)
        Assert-True ($mutedByButton -lt 0.02) "voice still audible after mute button (peak $mutedByButton)"
        $unmuteButton = Find-ButtonByHelpText -Element $settings -HelpText 'Unmute microphone'
        Assert-True ($null -ne $unmuteButton) 'button tooltip did not flip to Unmute microphone'

        # 4b. the input level meter must rest at zero while muted, even though the
        # voice keeps arriving at the capture device
        $barCondition = New-Object System.Windows.Automation.PropertyCondition(
            [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
            [System.Windows.Automation.ControlType]::ProgressBar)
        $meter = $settings.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $barCondition)
        Assert-True ($null -ne $meter) 'level meter progress bar not found'
        $mutedLevel = $meter.GetCurrentPattern([System.Windows.Automation.RangeValuePattern]::Pattern).Current.Value
        Write-Host ("4b. level meter while muted: {0:F4}" -f $mutedLevel)
        Assert-True ($mutedLevel -lt 0.02) "level meter shows input while muted (value $mutedLevel)"

        # 5. while muted, moving the mic volume slider must not unmute
        $sliderCondition = New-Object System.Windows.Automation.PropertyCondition(
            [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
            [System.Windows.Automation.ControlType]::Slider)
        $sliders = $settings.FindAll([System.Windows.Automation.TreeScope]::Descendants, $sliderCondition)
        $micSlider = $sliders[$sliders.Count - 1]  # last slider on the tab
        $micSlider.GetCurrentPattern([System.Windows.Automation.RangeValuePattern]::Pattern).SetValue(150)
        Start-Sleep -Milliseconds 800
        $mutedAfterSlider = Get-MaxPeak -DeviceName $secondaryName -Seconds 2
        Write-Host ("5. voice peak muted + slider moved to 150: {0:F4}" -f $mutedAfterSlider)
        Assert-True ($mutedAfterSlider -lt 0.02) "slider change unmuted the microphone (peak $mutedAfterSlider)"
        $micSlider.GetCurrentPattern([System.Windows.Automation.RangeValuePattern]::Pattern).SetValue(100)
        Close-DcsbSettings $settings

        # 6. the muted state persists to config.xml (1 s save debounce)
        Start-Sleep -Seconds 2
        $configText = Get-Content $script:ConfigPath -Raw
        Assert-True ($configText -match '<MicrophoneMuted>true</MicrophoneMuted>') 'config.xml does not persist MicrophoneMuted=true'
        Write-Host '6. config.xml contains <MicrophoneMuted>true</MicrophoneMuted>'

        # 7. muted survives an app restart; the unmute button brings the voice back
        Stop-Dcsb $process
        Start-Sleep -Seconds 1
        $process = Start-Dcsb
        $main = Get-DcsbMainWindow -ProcessId $process.Id
        Start-Sleep -Seconds 2
        $mutedAfterRestart = Get-MaxPeak -DeviceName $secondaryName -Seconds 2
        Write-Host ("7. voice peak after restart (still muted): {0:F4}" -f $mutedAfterRestart)
        Assert-True ($mutedAfterRestart -lt 0.02) "mute not restored from config after restart (peak $mutedAfterRestart)"

        $settings = Open-DcsbSettings $main
        Select-DcsbSettingsTab $settings 'Sound'
        $unmuteButton = Find-ButtonByHelpText -Element $settings -HelpText 'Unmute microphone'
        Assert-True ($null -ne $unmuteButton) 'unmute button not found after restart'
        Invoke-UIElement $unmuteButton
        Start-Sleep -Milliseconds 800
        $restored = Get-MaxPeak -DeviceName $secondaryName -Seconds 2
        Write-Host ("8. voice peak after unmute button: {0:F4}" -f $restored)
        Assert-True ($restored -gt 0.1) "voice did not come back after unmuting (peak $restored)"

        # 8b. after unmuting, the level meter shows the voice again
        $meter = $settings.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $barCondition)
        $unmutedLevel = $meter.GetCurrentPattern([System.Windows.Automation.RangeValuePattern]::Pattern).Current.Value
        Write-Host ("8b. level meter after unmute: {0:F4}" -f $unmutedLevel)
        Assert-True ($unmutedLevel -gt 0.1) "level meter shows no input after unmute (value $unmutedLevel)"
        Close-DcsbSettings $settings

        Stop-Dcsb $process
    } finally {
        Stop-Voice
    }
}
