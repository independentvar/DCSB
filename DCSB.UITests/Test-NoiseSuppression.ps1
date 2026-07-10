# End-to-end test of noise suppression on the microphone leg, covering both
# suppressors. DCSB's microphone is set to VB-Cable's "CABLE Output"; continuous
# white noise rendered into "CABLE Input" stands in for background noise on the
# user's mic. With suppression off the noise passes to the secondary output;
# selecting Fast (rnnoise) or High quality (DeepFilterNet3) must cut the level
# dramatically while synthesized speech still gets through. Also covers:
# soundboard sounds bypassing the suppressor, persistence of the selected mode
# to config.xml, and the suppressor being restored from config after an app
# restart. Skipped when VB-Cable is not installed or no quiet meterable output
# exists.
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
    # real voice that the suppressors' speech models should let through.
    param([Parameter(Mandatory)] [string]$Path)
    $text = 'The quick brown fox jumps over the lazy dog. Testing noise suppression, one two three four five.'
    powershell.exe -NoProfile -Command "Add-Type -AssemblyName System.Speech; `$s = New-Object System.Speech.Synthesis.SpeechSynthesizer; `$s.SetOutputToWaveFile('$Path'); `$s.Speak('$text'); `$s.Dispose()" | Out-Null
    if (-not (Test-Path $Path)) { throw 'speech synthesis produced no wav file' }
}

function Assert-SpeechPasses {
    # plays synthesized speech into the cable and asserts it reaches the
    # secondary output despite the active suppressor
    param(
        [Parameter(Mandatory)] $CableIn,
        [Parameter(Mandatory)] [string]$SecondaryName,
        [Parameter(Mandatory)] [string]$Label
    )
    $speechWav = Join-Path ([IO.Path]::GetTempPath()) 'dcsb-noise-suppression-speech.wav'
    New-SpeechWav -Path $speechWav
    # WaveFileReader, not AudioFileReader: the helpers only load NAudio.Core
    # and NAudio.Wasapi, and the SAPI wav is plain PCM anyway
    $speechReader = New-Object NAudio.Wave.WaveFileReader($speechWav)
    $speechOut = New-Object NAudio.Wave.WasapiOut($CableIn, [NAudio.CoreAudioApi.AudioClientShareMode]::Shared, $true, 50)
    try {
        $speechOut.Init($speechReader)
        $speechOut.Play()
        Start-Sleep -Milliseconds 500
        $speechPeak = Get-MaxPeak -DeviceName $SecondaryName -Seconds 3
        Write-Host ("{0}: speech peak with suppression on: {1:F4}" -f $Label, $speechPeak)
        Assert-True ($speechPeak -gt 0.08) "speech was suppressed along with the noise (peak $speechPeak)"
    } finally {
        $speechOut.Stop(); $speechOut.Dispose(); $speechReader.Dispose()
        Remove-Item $speechWav -Force -ErrorAction SilentlyContinue
    }
}

function Select-SuppressionMode {
    # picks one of the "Noise:" radio options on the settings Sound tab
    param([Parameter(Mandatory)] $Settings, [Parameter(Mandatory)] [string]$ItemName)
    $item = Find-DescendantByName -Element $Settings -Name $ItemName
    Assert-True ($null -ne $item) "noise suppression option '$ItemName' not found on the Sound tab"
    $item.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
}

Invoke-UITest -Name 'NoiseSuppressionModes' -Body {
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

        # 2. Fast (rnnoise) cuts the noise floor hard (give it a moment to adapt)
        $settings = Open-DcsbSettings $main
        Select-DcsbSettingsTab $settings 'Sound'
        Select-SuppressionMode $settings 'Fast (RNNoise)'
        Start-Sleep -Seconds 3
        $fastSuppressed = Get-MaxPeak -DeviceName $secondaryName -Seconds 2
        Write-Host ("2. noise peak, Fast: {0:F4}" -f $fastSuppressed)
        Assert-True ($fastSuppressed -lt ($rawPeak * 0.5)) "Fast mode did not reduce the noise floor ($fastSuppressed vs $rawPeak)"

        # 2b. speech survives Fast
        Stop-Noise
        Assert-SpeechPasses -CableIn $cableIn -SecondaryName $secondaryName -Label '2b (Fast)'
        Start-Noise -Device $cableIn

        # 3. High quality (DeepFilterNet3) does the same; model load can take a
        # few seconds, so allow extra settling time
        Select-SuppressionMode $settings 'High quality (DeepFilterNet3)'
        Start-Sleep -Seconds 6
        $hqSuppressed = Get-MaxPeak -DeviceName $secondaryName -Seconds 2
        Write-Host ("3. noise peak, High quality: {0:F4}" -f $hqSuppressed)
        Assert-True ($hqSuppressed -lt ($rawPeak * 0.5)) "High quality mode did not reduce the noise floor ($hqSuppressed vs $rawPeak)"

        # 3b. speech survives High quality
        Stop-Noise
        Assert-SpeechPasses -CableIn $cableIn -SecondaryName $secondaryName -Label '3b (High quality)'
        Start-Noise -Device $cableIn

        # 4. soundboard sounds bypass the suppressor: a sound must still meter
        # at full strength while High quality suppression is on
        Close-DcsbSettings $settings
        Invoke-PlayTestSound $main
        $soundPeak = Get-MaxPeak -DeviceName $secondaryName -Seconds 1.5
        Write-Host ("4. soundboard sound peak with suppression on: {0:F4}" -f $soundPeak)
        Assert-True ($soundPeak -gt 0.1) "soundboard sound was suppressed too (peak $soundPeak)"

        # 5. the selected mode persists (1 s save debounce)
        Start-Sleep -Seconds 2
        $configText = Get-Content $script:ConfigPath -Raw
        Assert-True ($configText -match '<NoiseSuppressionMode>HighQuality</NoiseSuppressionMode>') 'config.xml does not persist NoiseSuppressionMode=HighQuality'
        Write-Host '5. config.xml contains <NoiseSuppressionMode>HighQuality</NoiseSuppressionMode>'

        # 6. suppression survives an app restart (SoundManager builds the mic
        # chain from config alone)
        Stop-Dcsb $process
        Start-Sleep -Seconds 1
        $process = Start-Dcsb
        $main = Get-DcsbMainWindow -ProcessId $process.Id
        Start-Sleep -Seconds 6
        $afterRestart = Get-MaxPeak -DeviceName $secondaryName -Seconds 2
        Write-Host ("6. noise peak after restart (still suppressed): {0:F4}" -f $afterRestart)
        Assert-True ($afterRestart -lt ($rawPeak * 0.5)) "suppression not restored after restart ($afterRestart vs $rawPeak)"

        # 7. turning it off brings the raw noise back live
        $settings = Open-DcsbSettings $main
        Select-DcsbSettingsTab $settings 'Sound'
        Select-SuppressionMode $settings 'No suppression'
        Start-Sleep -Milliseconds 800
        $restored = Get-MaxPeak -DeviceName $secondaryName -Seconds 2
        Write-Host ("7. noise peak, suppression off again: {0:F4}" -f $restored)
        Assert-True ($restored -gt 0.1) "noise did not come back after disabling suppression (peak $restored)"
        Close-DcsbSettings $settings

        Stop-Dcsb $process
    } finally {
        Stop-Noise
    }
}
