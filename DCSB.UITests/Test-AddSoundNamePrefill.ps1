# Verifies that selecting file/s in the Sound dialog prefills the empty Name
# field with the first file's name (without extension), and that a later file
# selection does not overwrite a name that is already set.
. "$PSScriptRoot\UITestHelpers.ps1"

Invoke-UITest -Name 'Sound dialog prefills name from the selected file' -Body {
    $wavs = @(Get-ChildItem (Join-Path $env:windir 'Media') -Filter *.wav | Select-Object -First 2)
    if ($wavs.Count -lt 2) { throw 'SKIP: needs at least two .wav files under %windir%\Media' }

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

    # a new sound has an empty name - selecting a file must prefill it
    $browseButton = Find-DescendantByName $soundWindow '...'
    Assert-True ($null -ne $browseButton) "browse ('...') button not found in the Sound dialog"
    Invoke-UIElement $browseButton
    $fileDialog = Wait-DcsbWindow $main 'Choose sound file/s'
    Select-OpenFileDialogFile $fileDialog $wavs[0].FullName

    $expected = [IO.Path]::GetFileNameWithoutExtension($wavs[0].FullName)
    Assert-True (Wait-TopEditValue $soundWindow $expected) `
        "name was not prefilled with '$expected' (got '$(Get-TopEditValue $soundWindow)')"
    Write-Host "  name prefilled: '$expected'"

    # selecting a different file must keep the already-set name
    Invoke-UIElement $browseButton
    $fileDialog = Wait-DcsbWindow $main 'Choose sound file/s'
    Select-OpenFileDialogFile $fileDialog $wavs[1].FullName
    Assert-True (Wait-AnyEditContains $soundWindow $wavs[1].FullName) `
        "Sound dialog did not pick up the second file '$($wavs[1].FullName)'"
    Assert-True ((Get-TopEditValue $soundWindow) -eq $expected) `
        "name '$expected' was overwritten after selecting another file (got '$(Get-TopEditValue $soundWindow)')"
    Write-Host "  name kept after second selection: '$expected'"

    $soundWindow.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern).Close()
    Stop-Dcsb $process
}
