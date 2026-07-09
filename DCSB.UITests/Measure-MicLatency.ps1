# Measures DCSB's microphone passthrough latency: renders a 440 Hz tone into
# "CABLE Input" (DCSB's mic is "CABLE Output") and, in a tight polling loop,
# timestamps (a) the tone appearing on the cable's own peak meter and (b) it
# arriving on DCSB's secondary output endpoint. Latency = b - a, which excludes
# the tone generator's own output buffering (the number still includes VB-Cable's
# internal hop and peak-meter update granularity, so treat it as an upper bound
# and compare runs against each other rather than reading it as absolute truth).
#
# Manual measurement tool, not a pass/fail test - the Measure- prefix keeps it
# out of Run-UITests.ps1's Test-*.ps1 glob. Needs VB-Cable, a quiet meterable
# render endpoint, and a Release build:
#
#   pwsh -File .\Measure-MicLatency.ps1 [-Trials 10]
param([int]$Trials = 10)

$ErrorActionPreference = 'Stop'
. "$PSScriptRoot\UITestHelpers.ps1"

Initialize-UITestContext
Stop-AllDcsb
$backup = Backup-DcsbConfig

$cableInName = 'CABLE Input (VB-Audio Virtual Cable)'
$cableOutName = 'CABLE Output (VB-Audio Virtual Cable)'

$enumerator = New-Object NAudio.CoreAudioApi.MMDeviceEnumerator
$render = $enumerator.EnumerateAudioEndPoints([NAudio.CoreAudioApi.DataFlow]::Render, [NAudio.CoreAudioApi.DeviceState]::Active)
$cableIn = $render | Where-Object { $_.FriendlyName -eq $cableInName } | Select-Object -First 1
if (-not $cableIn) { throw 'VB-Cable not installed.' }

# quiet, meterable, non-cable endpoint (Steam Streaming Speakers preferred)
$secondary = $null
$candidates = @($render | Where-Object { $_.FriendlyName -like '*Steam Streaming*' }) +
              @($render | Where-Object { $_.FriendlyName -notlike '*VB-Audio*' -and $_.FriendlyName -notlike '*Steam Streaming*' })
foreach ($candidate in $candidates) {
    $ambient = Get-MaxPeak -DeviceName $candidate.FriendlyName -Seconds 1
    if ($ambient -lt 0.02) { $secondary = $candidate; break }
    Write-Host "skipping $($candidate.FriendlyName): ambient peak $ambient"
}
if (-not $secondary) { throw 'No quiet render endpoint available.' }
Write-Host "Metered output: $($secondary.FriendlyName)"

$config = New-DcsbConfig -PrimaryOutput 'Disabled'
$config.SecondaryOutput = $secondary.FriendlyName
$config.SecondaryDeviceVolume = 100
$config.MicrophoneInput = $cableOutName
$config.MicrophoneVolume = 100
$config.PresetCollection.Add((New-DcsbPreset -Name 'LatencyProbe'))
Save-DcsbConfigModel $config

$process = $null
$results = @()
try {
    $process = Start-Dcsb
    Start-Sleep -Seconds 3   # app up, mic attached

    $cableMeter = $cableIn.AudioMeterInformation
    $outMeter = $secondary.AudioMeterInformation

    for ($t = 0; $t -lt $Trials; $t++) {
        # wait for both meters to be silent so the previous trial has drained
        $deadline = [DateTime]::UtcNow.AddSeconds(5)
        while (($cableMeter.MasterPeakValue -gt 0.02 -or $outMeter.MasterPeakValue -gt 0.02) -and [DateTime]::UtcNow -lt $deadline) {
            Start-Sleep -Milliseconds 50
        }
        Start-Sleep -Milliseconds 200

        $gen = New-Object NAudio.Wave.SampleProviders.SignalGenerator(48000, 2)
        $gen.Frequency = 440
        $gen.Gain = 0.5
        $wave = New-Object NAudio.Wave.SampleProviders.SampleToWaveProvider($gen)
        $tone = New-Object NAudio.Wave.WasapiOut($cableIn, [NAudio.CoreAudioApi.AudioClientShareMode]::Shared, $true, 20)
        $tone.Init($wave)

        $sw = [System.Diagnostics.Stopwatch]::StartNew()
        $tone.Play()

        $tCable = -1.0; $tOut = -1.0
        while ($sw.ElapsedMilliseconds -lt 2000 -and ($tCable -lt 0 -or $tOut -lt 0)) {
            if ($tCable -lt 0 -and $cableMeter.MasterPeakValue -gt 0.05) { $tCable = $sw.Elapsed.TotalMilliseconds }
            if ($tOut -lt 0 -and $outMeter.MasterPeakValue -gt 0.05) { $tOut = $sw.Elapsed.TotalMilliseconds }
        }
        $tone.Stop(); $tone.Dispose()

        if ($tCable -lt 0 -or $tOut -lt 0) {
            Write-Host ("trial {0}: TIMEOUT (cable={1:F1} out={2:F1})" -f ($t + 1), $tCable, $tOut)
            continue
        }
        $latency = $tOut - $tCable
        $results += $latency
        Write-Host ("trial {0}: tone on cable at {1,7:F1} ms, on output at {2,7:F1} ms -> app latency {3,6:F1} ms" -f ($t + 1), $tCable, $tOut, $latency)
    }
} finally {
    if ($process) { Stop-Dcsb $process }
    Stop-AllDcsb
    Restore-DcsbConfig -BackupPath $backup
    $enumerator.Dispose()
}

if ($results.Count) {
    $sorted = $results | Sort-Object
    $median = $sorted[[int][Math]::Floor($sorted.Count / 2)]
    Write-Host ''
    Write-Host ("RESULT: median {0:F1} ms  min {1:F1} ms  max {2:F1} ms  ({3} trials)" -f $median, $sorted[0], $sorted[-1], $results.Count)
} else {
    Write-Host 'RESULT: no successful trials'
    exit 1
}
