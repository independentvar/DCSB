# End-to-end test of rnnoise noise suppression on the microphone leg. DCSB's
# microphone is set to VB-Cable's "CABLE Output"; continuous white noise
# rendered into "CABLE Input" stands in for background noise on the user's mic.
# With "Suppress background noise" off the noise passes to the secondary output;
# turning it on must cut the level dramatically (rnnoise treats stationary noise
# as noise). Covers: the settings checkbox toggling live in both directions,
# soundboard sounds staying unaffected, persistence to config.xml, and the
# suppressor being restored from config after an app restart. Skipped when
# VB-Cable is not installed or no quiet meterable output exists.
. "$PSScriptRoot\UITestHelpers.ps1"

$script:NoiseOut = $null

function Start-Noise {
    param([Parameter(Mandatory)] $Device)
    Stop-Noise
    $gen = New-Object NAudio.Wave.SampleProviders.SignalGenerator(48000, 2)
    $gen.Type = [NAudio.Wave.SampleProviders.SignalGeneratorType]::White
    $gen.Gain = 0.25
    $wave = New-Object NAudio.Wave.SampleProviders.SampleToWaveProvider($gen)
    $script:NoiseOut = New-Object NAudio.Wave.WasapiOut($Device, [NAudio.CoreAudioApi.AudioClientShareMode]::Shared, $true, 50)
    $script:NoiseOut.Init($wave)
    $script:NoiseOut.Play()
}

function Stop-Noise {
    if ($script:NoiseOut) {
        $script:NoiseOut.Stop()
        $script:NoiseOut.Dispose()
        $script:NoiseOut = $null
    }
}

function New-SpeechWav {
    # Renders synthesized speech to a wav via Windows PowerShell (System.Speech
    # lives in the .NET Framework GAC; pwsh cannot load it) - a stand-in for a
    # real voice that rnnoise's speech model should let through.
    param([Parameter(Mandatory)] [string]$Path)
    $text = 'The quick brown fox jumps over the lazy dog. Testing noise suppression, one two three four five.'
    powershell.exe -NoProfile -Command "Add-Type -AssemblyName System.Speech; `$s = New-Object System.Speech.Synthesis.SpeechSynthesizer; `$s.SetOutputToWaveFile('$Path'); `$s.Speak('$text'); `$s.Dispose()" | Out-Null
    if (-not (Test-Path $Path)) { throw 'speech synthesis produced no wav file' }
}

function Get-SuppressionCheckbox {
    param([Parameter(Mandatory)] $Settings)
    $checkbox = Find-DescendantByName -Element $Settings -Name 'Suppress background noise'
    Assert-True ($null -ne $checkbox) 'Suppress background noise checkbox not found on the Sound tab'
    return $checkbox
}

function Set-Checkbox {
    param([Parameter(Mandatory)] $Checkbox, [Parameter(Mandatory)] [bool]$Checked)
    $pattern = $Checkbox.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern)
    if (($pattern.Current.ToggleState -eq [System.Windows.Automation.ToggleState]::On) -ne $Checked) {
        $pattern.Toggle()
    }
}

Invoke-UITest -Name 'NoiseSuppressionReducesNoiseFloor' -Body {
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
            Start-Noise -Device $candidate
            $peak = Get-MaxPeak -DeviceName $candidate.FriendlyName -Seconds 1
            Stop-Noise
            if ($peak -gt 0.05) { $secondaryName = $candidate.FriendlyName; break }
            Write-Host "skipping $($candidate.FriendlyName): meter does not register (peak $peak)"
        } catch { Stop-Noise }
    }
    if (-not $secondaryName) { throw 'SKIP: no quiet, meterable non-cable render endpoint.' }
    Write-Host "Secondary output for the test: $secondaryName"

    $config = New-DcsbConfig -PrimaryOutput 'Disabled'
    $config.SecondaryOutput = $secondaryName
    $config.SecondaryDeviceVolume = 100
    $config.MicrophoneInput = $cableOutName
    $config.MicrophoneVolume = 100
    $preset = New-DcsbPreset -Name 'UITest'
    $preset.SoundCollection.Add((New-DcsbSound -Name $script:TestSoundName))
    $config.PresetCollection.Add($preset)
    Save-DcsbConfigModel $config

    try {
        $process = Start-Dcsb
        $main = Get-DcsbMainWindow -ProcessId $process.Id
        Start-Sleep -Seconds 2

        # 0. the endpoint meter measures the whole device, not just DCSB - if other
        # system audio started on the chosen endpoint since selection, every later
        # reading would be polluted, so bail out as a skip rather than a false fail
        $ambient = Get-MaxPeak -DeviceName $secondaryName -Seconds 2
        if ($ambient -gt 0.02) { throw "SKIP: ambient audio appeared on $secondaryName (peak $ambient)." }

        # 1. baseline: white noise passes through the mic leg unsuppressed
        Start-Noise -Device $cableIn
        Start-Sleep -Milliseconds 800
        $rawPeak = Get-MaxPeak -DeviceName $secondaryName -Seconds 2
        Write-Host ("1. noise peak, suppression off: {0:F4}" -f $rawPeak)
        Assert-True ($rawPeak -gt 0.1) "noise did not reach the secondary output (peak $rawPeak)"

        # 2. enabling suppression cuts the noise floor hard (give rnnoise a
        # moment to adapt before metering)
        $settings = Open-DcsbSettings $main
        Select-DcsbSettingsTab $settings 'Sound'
        $checkbox = Get-SuppressionCheckbox $settings
        Set-Checkbox $checkbox $true
        Start-Sleep -Seconds 3
        $suppressed = Get-MaxPeak -DeviceName $secondaryName -Seconds 2
        Write-Host ("2. noise peak, suppression on: {0:F4}" -f $suppressed)
        Assert-True ($suppressed -lt ($rawPeak * 0.5)) "suppression did not reduce the noise floor ($suppressed vs $rawPeak)"

        # 2b. speech survives suppression: synthesized speech into the cable must
        # still come out of the mic leg while the white noise would not
        Stop-Noise
        $speechWav = Join-Path ([IO.Path]::GetTempPath()) 'dcsb-noise-suppression-speech.wav'
        New-SpeechWav -Path $speechWav
        # WaveFileReader, not AudioFileReader: the helpers only load NAudio.Core
        # and NAudio.Wasapi, and the SAPI wav is plain PCM anyway
        $speechReader = New-Object NAudio.Wave.WaveFileReader($speechWav)
        $speechOut = New-Object NAudio.Wave.WasapiOut($cableIn, [NAudio.CoreAudioApi.AudioClientShareMode]::Shared, $true, 50)
        try {
            $speechOut.Init($speechReader)
            $speechOut.Play()
            Start-Sleep -Milliseconds 500
            $speechPeak = Get-MaxPeak -DeviceName $secondaryName -Seconds 3
            Write-Host ("2b. speech peak with suppression on: {0:F4}" -f $speechPeak)
            Assert-True ($speechPeak -gt 0.08) "speech was suppressed along with the noise (peak $speechPeak)"
        } finally {
            $speechOut.Stop(); $speechOut.Dispose(); $speechReader.Dispose()
            Remove-Item $speechWav -Force -ErrorAction SilentlyContinue
        }
        Start-Noise -Device $cableIn

        # 3. soundboard sounds bypass the suppressor: a sound must still meter
        # at full strength while suppression is on
        Close-DcsbSettings $settings
        Invoke-PlayTestSound $main
        $soundPeak = Get-MaxPeak -DeviceName $secondaryName -Seconds 1.5
        Write-Host ("3. soundboard sound peak with suppression on: {0:F4}" -f $soundPeak)
        Assert-True ($soundPeak -gt 0.1) "soundboard sound was suppressed too (peak $soundPeak)"

        # 4. the setting persists (1 s save debounce)
        Start-Sleep -Seconds 2
        $configText = Get-Content $script:ConfigPath -Raw
        Assert-True ($configText -match '<NoiseSuppression>true</NoiseSuppression>') 'config.xml does not persist NoiseSuppression=true'
        Write-Host '4. config.xml contains <NoiseSuppression>true</NoiseSuppression>'

        # 5. suppression survives an app restart (SoundManager builds the mic
        # chain from config alone)
        Stop-Dcsb $process
        Start-Sleep -Seconds 1
        $process = Start-Dcsb
        $main = Get-DcsbMainWindow -ProcessId $process.Id
        Start-Sleep -Seconds 3
        $afterRestart = Get-MaxPeak -DeviceName $secondaryName -Seconds 2
        Write-Host ("5. noise peak after restart (still suppressed): {0:F4}" -f $afterRestart)
        Assert-True ($afterRestart -lt ($rawPeak * 0.5)) "suppression not restored after restart ($afterRestart vs $rawPeak)"

        # 6. turning it off brings the raw noise back live
        $settings = Open-DcsbSettings $main
        Select-DcsbSettingsTab $settings 'Sound'
        $checkbox = Get-SuppressionCheckbox $settings
        Set-Checkbox $checkbox $false
        Start-Sleep -Milliseconds 800
        $restored = Get-MaxPeak -DeviceName $secondaryName -Seconds 2
        Write-Host ("6. noise peak, suppression off again: {0:F4}" -f $restored)
        Assert-True ($restored -gt 0.1) "noise did not come back after disabling suppression (peak $restored)"
        Close-DcsbSettings $settings

        Stop-Dcsb $process
    } finally {
        Stop-Noise
    }
}
