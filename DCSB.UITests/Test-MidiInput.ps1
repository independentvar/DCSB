# Verifies the user-facing MIDI workflow is present and remains opt-in by default.
# Hardware MIDI delivery is covered manually with a controller or loopMIDI; this test
# exercises the same settings and sound-editor controls a user uses to configure it.
. "$PSScriptRoot\UITestHelpers.ps1"

Invoke-UITest -Name 'MIDI input defaults to Disabled and hides per-sound binding' -Body {
    $deviceNames = @(Get-OutputDeviceNames)
    $primary = if ($deviceNames.Count) { $deviceNames[0] } else { 'Disabled' }
    New-DcsbTestConfig -PrimaryOutput $primary

    $process = Start-Dcsb
    $main = Get-DcsbMainWindow -ProcessId $process.Id
    Set-DcsbForeground $main

    $settings = Open-DcsbSettings $main
    Select-DcsbSettingsTab $settings 'Input'
    $condition = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::AutomationIdProperty, 'MidiInputDevice')
    $midiDevice = $settings.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $condition)
    Assert-True ($null -ne $midiDevice) 'MIDI input device selector was not found on the Input settings tab'
    Close-DcsbSettings $settings

    $addButton = Find-ButtonByHelpText $main 'Add Sound'
    Assert-True ($null -ne $addButton) "'Add Sound' button not found in the main window"
    Invoke-UIElement $addButton
    $soundWindow = Wait-DcsbWindow $main 'Sound'
    $midiLabel = Find-DescendantByName $soundWindow 'MIDI:'
    Assert-True ($null -eq $midiLabel) 'MIDI binding field was shown while MIDI input was Disabled'
    $soundWindow.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern).Close()
    Stop-Dcsb $process
}
