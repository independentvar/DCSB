# Verifies the single-instance handoff used after UAC accepts the admin restart.
# The replacement is intentionally launched at the same integrity level here:
# secure-desktop UAC consent cannot be automated, while the mutex race is identical.
. "$PSScriptRoot\UITestHelpers.ps1"

Invoke-UITest -Name 'Admin restart replacement waits for the original instance to exit' -Body {
    New-DcsbTestConfig -PrimaryOutput 'Disabled'
    $original = Start-Dcsb
    $replacement = $null
    try {
        $replacement = Start-Process $script:ExePath -ArgumentList '--restart-elevated' -PassThru
        Start-Sleep -Seconds 2
        $replacement.Refresh()
        Assert-True (-not $replacement.HasExited) `
            'restart replacement exited while the original still owned the single-instance mutex'

        Stop-Dcsb $original
        $deadline = (Get-Date).AddSeconds(15)
        $window = $null
        while ((Get-Date) -lt $deadline -and -not $window) {
            Start-Sleep -Milliseconds 400
            $replacement.Refresh()
            if ($replacement.HasExited) {
                throw "restart replacement exited during handoff (exit code $($replacement.ExitCode))"
            }
            $window = Get-DcsbMainWindow -ProcessId $replacement.Id
        }

        Assert-True ($null -ne $window) `
            'restart replacement did not become the active DCSB instance after the original exited'
    } finally {
        if ($replacement) { Stop-Dcsb $replacement }
        if ($original -and -not $original.HasExited) { Stop-Dcsb $original }
    }
}
