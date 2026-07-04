# Verifies that switching the first output device at runtime (which disposes
# the playback engine and creates a new one) does not crash the app, and that
# playback still produces audio after switching back.
. "$PSScriptRoot\UITestHelpers.ps1"

Invoke-UITest -Name 'Live output device switch keeps app alive and playable' -Body {
    $deviceNames = Get-OutputDeviceNames
    Assert-True ($deviceNames.Count -gt 0) 'at least one active output device is required'
    $device = $deviceNames[0]
    Write-Host "  output device: '$device'"

    New-DcsbTestConfig -PrimaryOutput $device
    $process = Start-Dcsb
    $main = Get-DcsbMainWindow -ProcessId $process.Id

    $settings = Open-DcsbSettings $main
    $combos = Get-OutputDeviceCombos $settings

    Select-ComboItem $combos[0] 'Default Output Device'
    $process.Refresh()
    Assert-True (-not $process.HasExited) 'app crashed switching to Default Output Device'

    Select-ComboItem $combos[0] $device
    $process.Refresh()
    Assert-True (-not $process.HasExited) "app crashed switching back to '$device'"

    # the settings dialog is modal - it must be closed before the main window
    # accepts the double-click that plays the sound
    Close-DcsbSettings $settings

    Invoke-PlayTestSound $main
    $peak = Get-MaxPeak -DeviceName $device -Seconds 3
    Write-Host "  peak after device round-trip: $([math]::Round($peak, 4))"
    Assert-True ($peak -ge $script:PeakThreshold) "no audio on '$device' after switching devices (peak $peak)"

    Stop-Dcsb $process
}
