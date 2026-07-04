# Verifies that selecting a file in the Counter dialog prefills the empty Name
# field with the file's name (without extension), and that a later file
# selection does not overwrite a name that is already set.
. "$PSScriptRoot\UITestHelpers.ps1"

Invoke-UITest -Name 'Counter dialog prefills name from the selected file' -Body {
    $tempDir = Join-Path ([IO.Path]::GetTempPath()) ('dcsb-uitest-' + [Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $tempDir | Out-Null
    try {
        $firstFile = Join-Path $tempDir 'boss deaths.txt'
        $secondFile = Join-Path $tempDir 'other counter.txt'
        Set-Content $firstFile '0'
        Set-Content $secondFile '0'

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

        # a new counter has an empty name - selecting a file must prefill it
        $browseButton = Find-DescendantByName $counterWindow '...'
        Assert-True ($null -ne $browseButton) "browse ('...') button not found in the Counter dialog"
        Invoke-UIElement $browseButton
        $fileDialog = Wait-DcsbWindow $main 'Choose counter file'
        Select-OpenFileDialogFile $fileDialog $firstFile

        $expected = [IO.Path]::GetFileNameWithoutExtension($firstFile)
        Assert-True (Wait-TopEditValue $counterWindow $expected) `
            "name was not prefilled with '$expected' (got '$(Get-TopEditValue $counterWindow)')"
        Write-Host "  name prefilled: '$expected'"

        # selecting a different file must keep the already-set name
        Invoke-UIElement $browseButton
        $fileDialog = Wait-DcsbWindow $main 'Choose counter file'
        Select-OpenFileDialogFile $fileDialog $secondFile
        Assert-True (Wait-AnyEditContains $counterWindow $secondFile) `
            "Counter dialog did not pick up the second file '$secondFile'"
        Assert-True ((Get-TopEditValue $counterWindow) -eq $expected) `
            "name '$expected' was overwritten after selecting another file (got '$(Get-TopEditValue $counterWindow)')"
        Write-Host "  name kept after second selection: '$expected'"

        $counterWindow.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern).Close()
        Stop-Dcsb $process
    } finally {
        Remove-Item $tempDir -Recurse -Force -ErrorAction SilentlyContinue
    }
}
