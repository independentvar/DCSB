# Verifies the Counter dialog's read-only File box opens the file picker on a
# SINGLE left-click (previously it needed a double-click), matching the '...'
# button beside it.
. "$PSScriptRoot\UITestHelpers.ps1"

Invoke-UITest -Name 'Counter dialog File box reacts to a single click' -Body {
    $deviceNames = @(Get-OutputDeviceNames)
    $primary = if ($deviceNames.Count) { $deviceNames[0] } else { 'Disabled' }
    New-DcsbTestConfig -PrimaryOutput $primary

    $process = Start-Dcsb
    $main = Get-DcsbMainWindow -ProcessId $process.Id
    Set-DcsbForeground $main

    $addButton = Find-ButtonByHelpText $main 'Add Counter'
    Assert-True ($null -ne $addButton) "'Add Counter' button not found in the main window"
    Invoke-UIElement $addButton
    $counterWindow = Wait-DcsbWindow $main 'Counter'

    # the Counter dialog's Edit controls top-to-bottom are Name, File, Count,
    # Increment, Format; none have an automation id, so the File box is the
    # second one by vertical position
    $edits = @(Get-WindowEdits $counterWindow | Sort-Object { $_.Current.BoundingRectangle.Y })
    Assert-True ($edits.Count -ge 2) "Expected at least 2 text boxes in the Counter dialog, found $($edits.Count)."
    $fileBox = $edits[1]

    Set-DcsbForeground $counterWindow
    Invoke-ClickOn $fileBox
    $fileDialog = Wait-DcsbWindow $main 'Choose counter file'
    Write-Host '  single click on File opened the file picker'
    $fileDialog.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern).Close()
    Start-Sleep -Milliseconds 400

    $counterWindow.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern).Close()
    Stop-Dcsb $process
}
