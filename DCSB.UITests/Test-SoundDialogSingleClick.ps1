# Verifies the Sound dialog's File/s and Key/s boxes open their pickers on a
# SINGLE left-click. Both boxes are read-only; previously they required a
# double-click, now one click on the box (like clicking the '...' button) must
# be enough. Clicking the File/s box opens the 'Choose sound file/s' picker;
# clicking the Key/s box opens the (title-less) key-binding window.
. "$PSScriptRoot\UITestHelpers.ps1"

# The Sound dialog's Edit controls top-to-bottom are: [0] Name, [1] File/s,
# [2] Key/s (there is no automation id on any of them, so order by position).
function Get-SoundDialogEdits {
    param([Parameter(Mandatory)] $Window)
    $edits = @(Get-WindowEdits $Window | Sort-Object { $_.Current.BoundingRectangle.Y })
    if ($edits.Count -lt 3) { throw "Expected 3 text boxes in the Sound dialog, found $($edits.Count)." }
    return $edits
}

function Get-EditValue {
    param([Parameter(Mandatory)] $Edit)
    return $Edit.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).Current.Value
}

# The key-binding window has no title (WindowStyle=None) and is owned by the
# main window; find it by its 'Clear' button, which no other DCSB window has.
function Wait-BindKeysWindow {
    param([Parameter(Mandatory)] $MainWindow, [int]$TimeoutSec = 8)
    $deadline = (Get-Date).AddSeconds($TimeoutSec)
    do {
        $clear = Find-DescendantByName $MainWindow 'Clear'
        if ($clear) { return $true }
        Start-Sleep -Milliseconds 300
    } while ((Get-Date) -lt $deadline)
    return $false
}

Invoke-UITest -Name 'Sound dialog File/s and Key/s boxes react to a single click' -Body {
    $deviceNames = @(Get-OutputDeviceNames)
    $primary = if ($deviceNames.Count) { $deviceNames[0] } else { 'Disabled' }
    New-DcsbTestConfig -PrimaryOutput $primary

    $process = Start-Dcsb
    $main = Get-DcsbMainWindow -ProcessId $process.Id
    Set-DcsbForeground $main

    $addButton = Find-ButtonByHelpText $main 'Add Sound'
    Assert-True ($null -ne $addButton) "'Add Sound' button not found in the main window"
    Invoke-UIElement $addButton
    $soundWindow = Wait-DcsbWindow $main 'Sound'

    # --- File/s box: a single click opens the file picker ---
    $filesBox = (Get-SoundDialogEdits $soundWindow)[1]
    Set-DcsbForeground $soundWindow
    Invoke-ClickOn $filesBox
    $fileDialog = Wait-DcsbWindow $main 'Choose sound file/s'
    Write-Host '  single click on File/s opened the file picker'
    # dismiss it without changing the sound so the Key/s check starts clean
    $fileDialog.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern).Close()
    Start-Sleep -Milliseconds 400

    # --- Key/s box: a single click opens the key-binding window ---
    # It must open on button RELEASE, not press (like the '...' button does). The
    # key-binding dialog captures mouse buttons through a global hook that runs
    # before this handler; if the box opened the dialog on button-DOWN, that same
    # click's release would be captured as the binding ("Left Click") and close
    # the window instantly. Opening on release means the hook processes (and
    # ignores) the release before the dialog is listening. Record the current
    # binding (a new sound gets an auto-assigned key) to prove it stays untouched.
    $keysBaseline = Get-EditValue (Get-SoundDialogEdits $soundWindow)[2]
    Set-DcsbForeground $soundWindow
    $keysRect = (Get-SoundDialogEdits $soundWindow)[2].Current.BoundingRectangle
    $kx = [int]($keysRect.X + $keysRect.Width / 2)
    $ky = [int]($keysRect.Y + $keysRect.Height / 2)
    [DcsbUiTest.Native]::SetCursorPos($kx, $ky) | Out-Null
    [DcsbUiTest.Native]::mouse_event(2, 0, 0, 0, [UIntPtr]::Zero)  # LEFTDOWN
    Start-Sleep -Milliseconds 300
    Assert-True (-not (Wait-BindKeysWindow $main 1)) `
        'key-binding window opened on mouse-down; it must open on release so the opening click is not captured as the binding'
    [DcsbUiTest.Native]::mouse_event(4, 0, 0, 0, [UIntPtr]::Zero)  # LEFTUP
    Assert-True (Wait-BindKeysWindow $main) `
        'single click on the Key/s box did not open the key-binding window'
    Write-Host '  single click on Key/s opened the key-binding window (on release)'

    # the opening click must not itself be captured: no mouse button may be bound
    $keysValue = Get-EditValue (Get-SoundDialogEdits $soundWindow)[2]
    Assert-True (($keysValue -eq $keysBaseline) -and -not ($keysValue -match 'Click|Mouse')) `
        "opening click changed the binding (was '$keysBaseline', now '$keysValue')"

    # close the key-binding window via its Cancel button (it ignores mouse clicks)
    $cancel = Find-DescendantByName $main 'Cancel'
    Assert-True ($null -ne $cancel) 'Cancel button not found in the key-binding window'
    Invoke-UIElement $cancel
    Start-Sleep -Milliseconds 400

    # cancelling leaves the original binding in place
    $keysValue = Get-EditValue (Get-SoundDialogEdits $soundWindow)[2]
    Assert-True ($keysValue -eq $keysBaseline) `
        "Key/s binding changed after cancelling (was '$keysBaseline', now '$keysValue')"

    $soundWindow.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern).Close()
    Stop-Dcsb $process
}
