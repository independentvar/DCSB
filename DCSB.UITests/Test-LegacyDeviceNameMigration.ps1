# Verifies that a device name truncated to 31 characters by the old WaveOut
# enumeration (versions <= 4.5.x) is upgraded to the full MMDevice friendly
# name in config.xml on startup.
. "$PSScriptRoot\UITestHelpers.ps1"

Invoke-UITest -Name 'Legacy truncated device name is migrated to full friendly name' -Body {
    $deviceNames = Get-OutputDeviceNames
    Assert-True ($deviceNames.Count -gt 0) 'at least one active output device is required'

    $longName = $deviceNames | Where-Object { $_.Length -gt 31 } | Select-Object -First 1
    if (-not $longName) {
        throw 'SKIP: no output device with a friendly name longer than 31 characters on this machine.'
    }
    $truncatedName = $longName.Substring(0, 31)
    Write-Host "  device: '$longName', stored as legacy '$truncatedName'"

    New-DcsbTestConfig -PrimaryOutput $truncatedName
    $process = Start-Dcsb

    # config saves are debounced (1s), poll for the rewritten value
    $migrated = Wait-ConfigPrimaryOutput -Expected $longName -TimeoutSec 10
    Assert-True $migrated "config PrimaryOutput was not upgraded to '$longName' (still '$(Get-ConfigPrimaryOutput)')"

    $process.Refresh()
    Assert-True (-not $process.HasExited) 'app must still be running after migration'
    Stop-Dcsb $process
}
