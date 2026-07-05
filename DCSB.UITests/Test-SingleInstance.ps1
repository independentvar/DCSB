# Verifies the single-instance guard: launching DCSB while another instance is
# running exits the new process and restores the existing instance's window.
. "$PSScriptRoot\UITestHelpers.ps1"

Invoke-UITest -Name 'Second instance exits and restores the first window' -Body {
    $deviceNames = @(Get-OutputDeviceNames)
    $primary = if ($deviceNames.Count) { $deviceNames[0] } else { 'Disabled' }
    New-DcsbTestConfig -PrimaryOutput $primary

    $process = Start-Dcsb
    $main = Get-DcsbMainWindow -ProcessId $process.Id
    $windowPattern = $main.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern)

    # minimize so the "show existing window" broadcast has an observable effect
    $windowPattern.SetWindowVisualState([System.Windows.Automation.WindowVisualState]::Minimized)
    Start-Sleep -Milliseconds 500
    Assert-True ($windowPattern.Current.WindowVisualState -eq [System.Windows.Automation.WindowVisualState]::Minimized) `
        'could not minimize the first instance'

    $second = Start-Process $script:ExePath -PassThru
    Assert-True ($second.WaitForExit(10000)) 'second instance did not exit within 10 seconds'
    $process.Refresh()
    Assert-True (-not $process.HasExited) 'first instance exited unexpectedly'
    Write-Host '  second instance exited, first instance still running'

    # the second launch must have asked the first instance to show its window
    $deadline = (Get-Date).AddSeconds(5)
    do {
        Start-Sleep -Milliseconds 250
        $state = $windowPattern.Current.WindowVisualState
    } while ($state -eq [System.Windows.Automation.WindowVisualState]::Minimized -and (Get-Date) -lt $deadline)
    Assert-True ($state -ne [System.Windows.Automation.WindowVisualState]::Minimized) `
        "first instance's window was not restored by the second launch"
    Write-Host "  first instance's window was restored"

    Stop-Dcsb $process
}
