# Verifies that double-clicking a sound actually produces signal on the
# selected output device (measured via the endpoint's WASAPI peak meter),
# including the interrupt-and-replay and replay-after-finish paths, which
# exercise stopping and restarting the shared WasapiOut instance.
. "$PSScriptRoot\UITestHelpers.ps1"

Invoke-UITest -Name 'Playing a sound produces audio on the selected output device' -Body {
    $deviceNames = Get-OutputDeviceNames
    Assert-True ($deviceNames.Count -gt 0) 'at least one active output device is required'
    $device = $deviceNames[0]
    Write-Host "  output device: '$device'"

    New-DcsbTestConfig -PrimaryOutput $device
    $process = Start-Dcsb
    $main = Get-DcsbMainWindow -ProcessId $process.Id

    Invoke-PlayTestSound $main
    $peak = Get-MaxPeak -DeviceName $device -Seconds 3
    Write-Host "  peak after play: $([math]::Round($peak, 4))"
    Assert-True ($peak -ge $script:PeakThreshold) "no audio detected on '$device' after playing (peak $peak)"

    # interrupt mid-playback: with Overlap off this stops all mixer inputs and
    # restarts playback on the same output device instance
    Invoke-PlayTestSound $main
    Start-Sleep -Milliseconds 500
    Invoke-PlayTestSound $main
    $peak = Get-MaxPeak -DeviceName $device -Seconds 3
    Write-Host "  peak after interrupt+replay: $([math]::Round($peak, 4))"
    Assert-True ($peak -ge $script:PeakThreshold) "no audio after interrupting and replaying (peak $peak)"

    # replay after the sound has finished naturally
    Start-Sleep -Seconds 6
    Invoke-PlayTestSound $main
    $peak = Get-MaxPeak -DeviceName $device -Seconds 3
    Write-Host "  peak after replay post-finish: $([math]::Round($peak, 4))"
    Assert-True ($peak -ge $script:PeakThreshold) "no audio when replaying after playback finished (peak $peak)"

    $process.Refresh()
    Assert-True (-not $process.HasExited) 'app must still be running after playback'
    Stop-Dcsb $process
}
