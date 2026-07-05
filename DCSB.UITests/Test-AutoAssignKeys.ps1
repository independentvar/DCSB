# Verifies auto-assignment of keys to new sounds end-to-end: a new sound gets
# the first free number-row key, switching the key set to numpad in Settings ->
# Shortcuts changes what the next sound gets, and unchecking the feature stops
# assignment entirely. Assignments are verified in the saved config.xml.
. "$PSScriptRoot\UITestHelpers.ps1"

# reads the saved config with the app's own serializer (config saves are
# debounced ~1s, hence the polling)
function Get-DcsbConfigModel {
    $serializer = New-Object System.Xml.Serialization.XmlSerializer ([DCSB.Models.ConfigurationModel])
    $stream = [IO.File]::Open($script:ConfigPath, 'Open', 'Read', 'ReadWrite')
    try { return $serializer.Deserialize($stream) } finally { $stream.Dispose() }
}

function Wait-SavedSounds {
    param(
        [Parameter(Mandatory)] [int]$ExpectedCount,
        [Parameter(Mandatory)] [scriptblock]$Condition
    )
    $deadline = (Get-Date).AddSeconds(10)
    do {
        Start-Sleep -Milliseconds 500
        try {
            $sounds = (Get-DcsbConfigModel).PresetCollection[0].SoundCollection
            if ($sounds.Count -eq $ExpectedCount -and (& $Condition $sounds)) { return $true }
        } catch { }
    } while ((Get-Date) -lt $deadline)
    return $false
}

function Add-EmptySound {
    param([Parameter(Mandatory)] $MainWindow)
    $addButton = Find-ButtonByHelpText $MainWindow 'Add Sound'
    Assert-True ($null -ne $addButton) "'Add Sound' button not found in the main window"
    Invoke-UIElement $addButton
    $soundWindow = Wait-DcsbWindow $MainWindow 'Sound'
    $soundWindow.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern).Close()
    Start-Sleep -Milliseconds 300
}

Invoke-UITest -Name 'New sounds get auto-assigned keys per the Shortcuts settings' -Body {
    $deviceNames = @(Get-OutputDeviceNames)
    $primary = if ($deviceNames.Count) { $deviceNames[0] } else { 'Disabled' }
    New-DcsbTestConfig -PrimaryOutput $primary   # one sound named 'uitest-sound', no keys

    $process = Start-Dcsb
    $main = Get-DcsbMainWindow -ProcessId $process.Id
    Set-DcsbForeground $main

    # 1. defaults: the first added sound gets the first free number-row key
    Add-EmptySound $main
    Assert-True (Wait-SavedSounds 2 { param($sounds) ($sounds[1].Keys -join ',') -eq 'KEY_1' }) `
        "first added sound was not assigned KEY_1 (config: $((Get-DcsbConfigModel).PresetCollection[0].SoundCollection | ForEach-Object { $_.Keys -join '+' }))"
    Write-Host '  first added sound got KEY_1'

    # 2. switch the key set to numpad in Settings -> Shortcuts
    $settings = Open-DcsbSettings $main
    Select-DcsbSettingsTab $settings 'Shortcuts'
    $checkbox = Find-DescendantByName $settings 'Automatically assign keys to new sounds'
    Assert-True ($null -ne $checkbox) 'auto-assign checkbox not found on the Shortcuts tab'
    Assert-True ($checkbox.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern).Current.ToggleState -eq
        [System.Windows.Automation.ToggleState]::On) 'auto-assign checkbox is not checked by default'
    $numpadOption = Find-DescendantByName $settings 'Numpad (1-0)'
    Assert-True ($null -ne $numpadOption) "'Numpad (1-0)' option not found on the Shortcuts tab"
    $numpadOption.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
    Close-DcsbSettings $settings

    Add-EmptySound $main
    Assert-True (Wait-SavedSounds 3 { param($sounds) ($sounds[2].Keys -join ',') -eq 'NUMPAD1' }) `
        'sound added with the numpad key set was not assigned NUMPAD1'
    Write-Host '  after switching to numpad, next sound got NUMPAD1'

    # 3. disable the feature - the next sound must get no keys
    $settings = Open-DcsbSettings $main
    Select-DcsbSettingsTab $settings 'Shortcuts'
    $checkbox = Find-DescendantByName $settings 'Automatically assign keys to new sounds'
    $checkbox.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern).Toggle()
    Close-DcsbSettings $settings

    Add-EmptySound $main
    Assert-True (Wait-SavedSounds 4 { param($sounds) $sounds[3].Keys.Count -eq 0 }) `
        'sound added with auto-assign disabled still got keys assigned'
    Write-Host '  with auto-assign disabled, next sound got no keys'

    Stop-Dcsb $process
}
