# Verifies that the output device dropdowns in Settings -> Sound list every
# active render endpoint with its full (untruncated) friendly name, plus the
# Disabled and Default Output Device pseudo-entries.
. "$PSScriptRoot\UITestHelpers.ps1"

Invoke-UITest -Name 'Device dropdowns list full untruncated device names' -Body {
    $deviceNames = Get-OutputDeviceNames
    Assert-True ($deviceNames.Count -gt 0) 'at least one active output device is required'

    New-DcsbTestConfig -PrimaryOutput 'Default Output Device'
    $process = Start-Dcsb
    $main = Get-DcsbMainWindow -ProcessId $process.Id

    $settings = Open-DcsbSettings $main
    $combos = Get-OutputDeviceCombos $settings

    foreach ($combo in $combos) {
        $items = Get-ComboItemNames $combo
        Write-Host "  combo items: $($items -join ' | ')"
        Assert-True ($items -contains 'Disabled') "combo is missing the 'Disabled' entry"
        Assert-True ($items -contains 'Default Output Device') "combo is missing the 'Default Output Device' entry"
        foreach ($name in $deviceNames) {
            Assert-True ($items -contains $name) "combo is missing device '$name' (or lists it truncated)"
        }
        $realEntries = @($items | Where-Object { $_ -ne 'Disabled' -and $_ -ne 'Default Output Device' })
        Assert-True ($realEntries.Count -eq $deviceNames.Count) "combo lists $($realEntries.Count) devices, expected $($deviceNames.Count)"
    }

    Close-DcsbSettings $settings
    Stop-Dcsb $process
}
