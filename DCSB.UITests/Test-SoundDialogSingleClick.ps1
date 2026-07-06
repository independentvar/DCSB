# Verifies the Sound dialog's File/s and Key/s boxes react to a SINGLE left-click.
# File/s is a read-only Edit that opens the 'Choose sound file/s' picker. Key/s is
# now an inline ShortcutBox (no separate key-binding window): one click puts it into
# the listening state in place. The opening click must act on button RELEASE and must
# not be captured; then a click INSIDE the listening box binds that mouse button, and
# Escape cancels without recording anything.
. "$PSScriptRoot\UITestHelpers.ps1"

# The Sound dialog's Edit controls are now just [0] Name and [1] File/s (Key/s is
# a ShortcutBox, not an Edit). Order by vertical position; there are no automation
# ids on them.
function Get-SoundDialogEdits {
    param([Parameter(Mandatory)] $Window)
    $edits = @(Get-WindowEdits $Window | Sort-Object { $_.Current.BoundingRectangle.Y })
    if ($edits.Count -lt 2) { throw "Expected at least 2 text boxes in the Sound dialog, found $($edits.Count)." }
    return $edits
}

# Returns every Text element's Name in the window (labels and the ShortcutBox's
# visible text; collapsed template text is not in the automation tree).
function Get-DialogTexts {
    param([Parameter(Mandatory)] $Window)
    $textCondition = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
        [System.Windows.Automation.ControlType]::Text)
    return @($Window.FindAll([System.Windows.Automation.TreeScope]::Descendants, $textCondition) |
        ForEach-Object { $_.Current.Name })
}

# The Key/s ShortcutBox shows "Press keys…" only while it is capturing.
function Test-KeysListening {
    param([Parameter(Mandatory)] $Window)
    return [bool](@(Get-DialogTexts $Window) | Where-Object { $_ -like 'Press keys*' })
}

function Wait-KeysListening {
    param([Parameter(Mandatory)] $Window, [int]$TimeoutSec = 8)
    $deadline = (Get-Date).AddSeconds($TimeoutSec)
    do {
        if (Test-KeysListening $Window) { return $true }
        Start-Sleep -Milliseconds 300
    } while ((Get-Date) -lt $deadline)
    return $false
}

# True if any binding shown in the dialog names a mouse button - i.e. a click was
# wrongly captured. The dialog's other labels never contain these words.
function Test-DialogHasMouseBinding {
    param([Parameter(Mandatory)] $Window)
    return [bool](@(Get-DialogTexts $Window) | Where-Object { $_ -match 'Click|Mouse' })
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

    # The Key/s ShortcutBox has no automation element of its own; click it using the
    # File/s box's horizontal position (same grid column) at the Key/s label's row.
    Set-DcsbForeground $soundWindow
    $filesRect = (Get-SoundDialogEdits $soundWindow)[1].Current.BoundingRectangle
    $keysLabel = Find-DescendantByName $soundWindow 'Key/s:'
    Assert-True ($null -ne $keysLabel) "'Key/s:' label not found in the Sound dialog"
    $labelRect = $keysLabel.Current.BoundingRectangle
    $kx = [int]($filesRect.X + $filesRect.Width / 2)
    $ky = [int]($labelRect.Y + $labelRect.Height / 2)

    # --- Key/s box: a single click starts inline capture, on RELEASE not press ---
    # The global mouse hook runs before WPF sees the click; starting capture on
    # button-down would let that same click's release be captured as the binding
    # ("Left Click"). Starting on release means the hook processes the button before
    # capture arms, so the opening click is not recorded.
    [DcsbUiTest.Native]::SetCursorPos($kx, $ky) | Out-Null
    [DcsbUiTest.Native]::mouse_event(2, 0, 0, 0, [UIntPtr]::Zero)  # LEFTDOWN
    Start-Sleep -Milliseconds 300
    Assert-True (-not (Test-KeysListening $soundWindow)) `
        'Key/s box entered capture on mouse-down; it must start on release so the opening click is not captured as the binding'
    [DcsbUiTest.Native]::mouse_event(4, 0, 0, 0, [UIntPtr]::Zero)  # LEFTUP
    Assert-True (Wait-KeysListening $soundWindow) `
        'single click on the Key/s box did not start inline key capture'
    Assert-True (-not (Test-DialogHasMouseBinding $soundWindow)) `
        'the opening click was captured as a mouse-button binding'
    Write-Host '  single click on Key/s started inline capture (on release), uncaptured'

    # --- Escape cancels without recording anything ---
    Send-EscapeKey
    Start-Sleep -Milliseconds 300
    Assert-True (-not (Test-KeysListening $soundWindow)) 'Escape did not cancel capture'
    Assert-True (-not (Test-DialogHasMouseBinding $soundWindow)) `
        'Escape or the cancel left a mouse binding behind'
    Write-Host '  Escape cancelled capture'

    # --- clicking inside the listening box binds that mouse button ---
    # Start listening again, then a second click inside the box must be captured by
    # the hook (its point is inside the published binding region) and recorded as a
    # "Left Click" binding, ending capture without the box re-arming.
    Invoke-ClickAt $kx $ky
    Assert-True (Wait-KeysListening $soundWindow) 'Key/s box did not start capture on the second attempt'
    Invoke-ClickAt $kx $ky
    Start-Sleep -Milliseconds 400
    Assert-True (-not (Test-KeysListening $soundWindow)) `
        'clicking inside the listening box did not end capture (the box re-armed after binding)'
    Assert-True (Test-DialogHasMouseBinding $soundWindow) `
        'clicking inside the listening box did not bind the mouse button'
    Write-Host '  clicking inside the box bound the mouse button'

    $soundWindow.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern).Close()
    Stop-Dcsb $process
}
