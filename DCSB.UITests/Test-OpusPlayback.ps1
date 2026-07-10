# Verifies that an Ogg/Opus file (the format of Discord recordings and modern
# meme clips) actually plays through the app: a tone is encoded to .opus with
# Concentus (the same library the app decodes with, but through its encoder
# API, so the file is a genuine independent Opus stream), added as a sound, and
# double-clicked; the selected output's WASAPI peak meter must register it.
. "$PSScriptRoot\UITestHelpers.ps1"

function New-OpusToneFile {
    param([string]$Path)
    Add-Type -Path (Join-Path $script:BinDir 'Concentus.dll')
    Add-Type -Path (Join-Path $script:BinDir 'Concentus.Oggfile.dll')

    $rate = 48000; $channels = 2; $frames = $rate * 2
    $pcm = New-Object 'int16[]' ($frames * $channels)
    for ($i = 0; $i -lt $frames; $i++) {
        $value = [int16]([Math]::Sin(2 * [Math]::PI * 440 * $i / $rate) * 0.5 * 32767)
        $pcm[$i * 2] = $value
        $pcm[$i * 2 + 1] = $value
    }

    $stream = [IO.File]::Create($Path)
    try {
        $encoder = [Concentus.OpusCodecFactory]::CreateEncoder($rate, $channels, [Concentus.Enums.OpusApplication]::OPUS_APPLICATION_AUDIO, $null)
        $writer = New-Object Concentus.Oggfile.OpusOggWriteStream($encoder, $stream, $null, $rate, 0, $false)
        $writer.WriteSamples($pcm, 0, $pcm.Length)
        $writer.Finish()
    } finally {
        $stream.Dispose()
    }
}

Invoke-UITest -Name 'Ogg/Opus file plays and is audible' -Body {
    $deviceNames = Get-OutputDeviceNames
    Assert-True ($deviceNames.Count -gt 0) 'at least one active output device is required'
    $device = $deviceNames[0]
    Write-Host "  output device: '$device'"

    $opus = Join-Path ([IO.Path]::GetTempPath()) 'dcsb-uitest-tone.opus'
    New-OpusToneFile -Path $opus

    $config = New-DcsbConfig -PrimaryOutput $device
    $preset = New-DcsbPreset -Name 'UITest'
    $preset.SoundCollection.Add((New-DcsbSound -Name $script:TestSoundName -File $opus))
    $config.PresetCollection.Add($preset)
    Save-DcsbConfigModel $config

    try {
        $process = Start-Dcsb
        $main = Get-DcsbMainWindow -ProcessId $process.Id

        Invoke-PlayTestSound $main
        $peak = Get-MaxPeak -DeviceName $device -Seconds 3
        Write-Host "  peak after playing .opus: $([math]::Round($peak, 4))"
        Assert-True ($peak -ge $script:PeakThreshold) "no audio detected on '$device' after playing an opus file (peak $peak)"

        # the duration column proves the metadata path decodes opus too
        $row = Find-DescendantByName $main $script:TestSoundName
        Assert-True ($null -ne $row) 'sound row should exist'

        Stop-Dcsb $process
    } finally {
        Remove-Item $opus -Force -ErrorAction SilentlyContinue
    }
}
