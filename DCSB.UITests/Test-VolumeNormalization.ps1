# End-to-end test of volume normalization: two sounds containing the same
# 997 Hz tone mastered 20 dB apart (amplitude 0.6 vs 0.06). With normalization
# enabled both must reach the output at roughly the same level (the quiet one
# boosted, the loud one attenuated toward the -16 LUFS target); after disabling
# the feature through the settings checkbox they must differ by ~20 dB again,
# and the setting must persist to config.xml.
. "$PSScriptRoot\UITestHelpers.ps1"

function New-ToneWav {
    param([string]$Path, [double]$Amplitude)
    $rate = 44100; $seconds = 2.0; $freq = 997
    $count = [int]($rate * $seconds)
    $data = New-Object byte[] ($count * 2)
    for ($i = 0; $i -lt $count; $i++) {
        $sample = [int16]([Math]::Sin(2 * [Math]::PI * $freq * $i / $rate) * $Amplitude * 32767)
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

function Invoke-PlayNamedSound {
    param([Parameter(Mandatory)] $MainWindow, [Parameter(Mandatory)] [string]$Name)
    Set-DcsbForeground $MainWindow
    $row = Find-DescendantByName $MainWindow $Name
    if (-not $row) { throw "Sound row '$Name' not found in the main window." }
    Invoke-DoubleClickOn $row
}

Invoke-UITest -Name 'Volume normalization equalizes differently mastered sounds' -Body {
    # a currently-silent output whose peak meter registers
    $device = $null
    foreach ($candidate in Get-OutputDeviceNames) {
        $ambient = Get-MaxPeak -DeviceName $candidate -Seconds 1
        if ($ambient -lt 0.02) { $device = $candidate; break }
        Write-Host "skipping $($candidate): ambient audio (peak $ambient)"
    }
    if (-not $device) { throw 'SKIP: no quiet output device available.' }
    Write-Host "  output device: '$device'"

    $loudWav = Join-Path ([IO.Path]::GetTempPath()) 'dcsb-norm-loud.wav'
    $quietWav = Join-Path ([IO.Path]::GetTempPath()) 'dcsb-norm-quiet.wav'
    New-ToneWav -Path $loudWav -Amplitude 0.6
    New-ToneWav -Path $quietWav -Amplitude 0.06

    $config = New-DcsbConfig -PrimaryOutput $device
    $config.NormalizeVolume = $true
    $preset = New-DcsbPreset -Name 'UITest'
    $preset.SoundCollection.Add((New-DcsbSound -Name 'uitest-loud' -File $loudWav))
    $preset.SoundCollection.Add((New-DcsbSound -Name 'uitest-quiet' -File $quietWav))
    $config.PresetCollection.Add($preset)
    Save-DcsbConfigModel $config

    try {
        $process = Start-Dcsb
        $main = Get-DcsbMainWindow -ProcessId $process.Id
        Start-Sleep -Seconds 3  # background loudness prefetch (two 2 s wavs: instant)

        # warm-up: the very first double-click after launch is occasionally
        # swallowed while the window settles; play and discard one round
        Invoke-PlayNamedSound $main 'uitest-loud'
        Start-Sleep -Seconds 2

        # 1. normalization on: both tones must land at about the same level
        Invoke-PlayNamedSound $main 'uitest-loud'
        $loudPeak = Get-MaxPeak -DeviceName $device -Seconds 1.5
        Start-Sleep -Seconds 1.5  # let the 2 s tone finish
        Invoke-PlayNamedSound $main 'uitest-quiet'
        $quietPeak = Get-MaxPeak -DeviceName $device -Seconds 1.5
        Write-Host ("1. normalized peaks: loud {0:F4}, quiet {1:F4}" -f $loudPeak, $quietPeak)
        Assert-True ($loudPeak -gt 0.02 -and $quietPeak -gt 0.02) "both sounds must be audible ($loudPeak / $quietPeak)"
        $ratio = $quietPeak / $loudPeak
        Assert-True ($ratio -gt 0.6 -and $ratio -lt 1.67) "normalized levels should match within ~4 dB (ratio $([math]::Round($ratio, 3)))"

        # 2. disable via the settings checkbox
        $settings = Open-DcsbSettings $main
        Select-DcsbSettingsTab $settings 'Sound'
        $checkbox = Find-DescendantByName $settings 'Normalize volume across sounds'
        Assert-True ($null -ne $checkbox) 'normalization checkbox not found on the Sound tab'
        $toggle = $checkbox.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern)
        Assert-True ($toggle.Current.ToggleState -eq [System.Windows.Automation.ToggleState]::On) 'checkbox should reflect the enabled config'
        $toggle.Toggle()
        Close-DcsbSettings $settings
        Start-Sleep -Seconds 2  # config save debounce
        $configText = Get-Content $script:ConfigPath -Raw
        Assert-True ($configText -match '<NormalizeVolume>false</NormalizeVolume>') 'config.xml must persist NormalizeVolume=false'

        # 3. normalization off: raw 20 dB difference comes back
        Invoke-PlayNamedSound $main 'uitest-loud'
        $rawLoudPeak = Get-MaxPeak -DeviceName $device -Seconds 1.5
        Start-Sleep -Seconds 1.5
        Invoke-PlayNamedSound $main 'uitest-quiet'
        $rawQuietPeak = Get-MaxPeak -DeviceName $device -Seconds 1.5
        Write-Host ("3. raw peaks: loud {0:F4}, quiet {1:F4}" -f $rawLoudPeak, $rawQuietPeak)
        Assert-True ($rawLoudPeak -gt 0.02) "loud sound must be audible raw ($rawLoudPeak)"
        $rawRatio = $rawQuietPeak / $rawLoudPeak
        Assert-True ($rawRatio -lt 0.3) "without normalization the quiet clip must stay ~20 dB quieter (ratio $([math]::Round($rawRatio, 3)))"

        Stop-Dcsb $process
    } finally {
        Remove-Item $loudWav, $quietWav -Force -ErrorAction SilentlyContinue
    }
}
