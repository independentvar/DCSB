# Verifies the core deathcounter feature end-to-end: selecting a counter and
# pressing the main-window Increment/Decrement buttons changes its count and
# writes the value, rendered through the counter's Format, to its .txt file -
# the file an overlay/OBS text source reads. Also exercises the configured
# increment amount and negative counts (the decrement-below-zero path).
. "$PSScriptRoot\UITestHelpers.ps1"

Invoke-UITest -Name 'Counter increment/decrement writes the formatted count to its file' -Body {
    $tempDir = Join-Path ([IO.Path]::GetTempPath()) ('dcsb-uitest-' + [Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $tempDir | Out-Null
    try {
        $counterFile = Join-Path $tempDir 'deaths.txt'
        Set-Content $counterFile 'Deaths: 0' -NoNewline

        $deviceNames = @(Get-OutputDeviceNames)
        $primary = if ($deviceNames.Count) { $deviceNames[0] } else { 'Disabled' }

        # a counter with a non-trivial format and an increment step of 5, so the
        # test proves the format is applied and the configured step is honored
        $config = New-DcsbConfig -PrimaryOutput $primary
        $preset = New-DcsbPreset -Name 'UITest'
        $preset.CounterCollection.Add((New-DcsbCounter -Name 'uitest-counter' -File $counterFile -Format 'Deaths: {0}' -Increment 5))
        $config.PresetCollection.Add($preset)
        Save-DcsbConfigModel $config

        $process = Start-Dcsb
        $main = Get-DcsbMainWindow -ProcessId $process.Id

        # nothing is selected on startup - the toolbar buttons act on the
        # selected counter, so select the row first
        Select-DcsbListRow $main 'uitest-counter'

        $increment = Find-ButtonByHelpText $main 'Increment'
        Assert-True ($null -ne $increment) "'Increment' button not found in the main window"
        $decrement = Find-ButtonByHelpText $main 'Decrement'
        Assert-True ($null -ne $decrement) "'Decrement' button not found in the main window"

        # +5 -> "Deaths: 5"
        Invoke-UIElement $increment
        Assert-True (Wait-FileContent $counterFile 'Deaths: 5') `
            "incrementing did not write 'Deaths: 5' (file: '$([IO.File]::ReadAllText($counterFile))')"
        Write-Host '  increment wrote "Deaths: 5"'

        # +5 -> "Deaths: 10"
        Invoke-UIElement $increment
        Assert-True (Wait-FileContent $counterFile 'Deaths: 10') `
            "second increment did not write 'Deaths: 10' (file: '$([IO.File]::ReadAllText($counterFile))')"
        Write-Host '  second increment wrote "Deaths: 10"'

        # -5 -> "Deaths: 5"
        Invoke-UIElement $decrement
        Assert-True (Wait-FileContent $counterFile 'Deaths: 5') `
            "decrementing did not write 'Deaths: 5' (file: '$([IO.File]::ReadAllText($counterFile))')"
        Write-Host '  decrement wrote "Deaths: 5"'

        # decrement past zero: 5 -> 0 -> -5, exercising the negative-count path
        Invoke-UIElement $decrement
        Invoke-UIElement $decrement
        Assert-True (Wait-FileContent $counterFile 'Deaths: -5') `
            "decrementing below zero did not write 'Deaths: -5' (file: '$([IO.File]::ReadAllText($counterFile))')"
        Write-Host '  decrement below zero wrote "Deaths: -5"'

        # the running app must have persisted the count back through a reload:
        # a fresh instance reads -5 from the file and writes it straight back
        Stop-Dcsb $process
        $process = Start-Dcsb
        $main = Get-DcsbMainWindow -ProcessId $process.Id
        Assert-True (Wait-FileContent $counterFile 'Deaths: -5') `
            "counter did not round-trip -5 through a restart (file: '$([IO.File]::ReadAllText($counterFile))')"
        Write-Host '  count survived an app restart'

        $process.Refresh()
        Assert-True (-not $process.HasExited) 'app must still be running after counting'
        Stop-Dcsb $process
    } finally {
        Remove-Item $tempDir -Recurse -Force -ErrorAction SilentlyContinue
    }
}
