# Verifies preset switching: with two presets configured, the main window shows
# only the active preset's sounds, and choosing another preset from the
# "Preset: <name>" menu swaps the visible sound list and persists the selection
# to config.xml.
. "$PSScriptRoot\UITestHelpers.ps1"

Invoke-UITest -Name 'Switching presets swaps the visible sounds and is persisted' -Body {
    $deviceNames = @(Get-OutputDeviceNames)
    $primary = if ($deviceNames.Count) { $deviceNames[0] } else { 'Disabled' }

    $config = New-DcsbConfig -PrimaryOutput $primary
    $alpha = New-DcsbPreset -Name 'Alpha'
    $alpha.SoundCollection.Add((New-DcsbSound -Name 'alpha-sound'))
    $bravo = New-DcsbPreset -Name 'Bravo'
    $bravo.SoundCollection.Add((New-DcsbSound -Name 'bravo-sound'))
    $config.PresetCollection.Add($alpha)
    $config.PresetCollection.Add($bravo)
    Save-DcsbConfigModel $config   # SelectedPresetIndex defaults to 0 (Alpha)

    $process = Start-Dcsb
    $main = Get-DcsbMainWindow -ProcessId $process.Id
    Set-DcsbForeground $main

    # Alpha is active: its sound shows, Bravo's does not
    Assert-True ($null -ne (Find-DescendantByName $main 'alpha-sound')) `
        "Alpha's sound 'alpha-sound' is not shown while Alpha is the active preset"
    Assert-True ($null -eq (Find-DescendantByName $main 'bravo-sound')) `
        "Bravo's sound 'bravo-sound' is shown while Alpha is the active preset"
    Write-Host '  Alpha active: only alpha-sound is listed'

    Select-DcsbPreset $main -Index 1   # Bravo

    Assert-True (Wait-ConfigSelectedPresetIndex 1) `
        "switching to Bravo did not persist SelectedPresetIndex=1 (got $(Get-ConfigSelectedPresetIndex))"
    Write-Host '  selected preset index persisted as 1'

    # Bravo is now active: the lists swapped
    Assert-True ($null -ne (Find-DescendantByName $main 'bravo-sound')) `
        "Bravo's sound 'bravo-sound' is not shown after switching to Bravo"
    Assert-True ($null -eq (Find-DescendantByName $main 'alpha-sound')) `
        "Alpha's sound 'alpha-sound' is still shown after switching to Bravo"
    Write-Host '  Bravo active: list swapped to bravo-sound'

    # switch back to Alpha to confirm it is a live toggle, not a one-way move
    Select-DcsbPreset $main -Index 0   # Alpha
    Assert-True (Wait-ConfigSelectedPresetIndex 0) `
        "switching back to Alpha did not persist SelectedPresetIndex=0 (got $(Get-ConfigSelectedPresetIndex))"
    Assert-True ($null -ne (Find-DescendantByName $main 'alpha-sound')) `
        "Alpha's sound 'alpha-sound' is not shown after switching back to Alpha"
    Write-Host '  switched back to Alpha'

    $process.Refresh()
    Assert-True (-not $process.HasExited) 'app must still be running after switching presets'
    Stop-Dcsb $process
}
