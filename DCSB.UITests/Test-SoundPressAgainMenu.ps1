# Verifies the Sound dialog's Press again combo exposes every behavior, defaults
# new sounds to Pause / resume, updates its selection, and persists the choice.
. "$PSScriptRoot\UITestHelpers.ps1"

function Get-PressAgainCombo {
    param([Parameter(Mandatory)] $SoundWindow)
    $condition = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
        [System.Windows.Automation.ControlType]::ComboBox)
    $combos = $SoundWindow.FindAll([System.Windows.Automation.TreeScope]::Descendants, $condition)
    if ($combos.Count -ne 1) {
        throw "Expected one combo box in the Sound dialog, found $($combos.Count)."
    }
    return $combos[0]
}

function Get-SelectedComboItemName {
    param([Parameter(Mandatory)] $Combo)
    $selection = $Combo.GetCurrentPattern([System.Windows.Automation.SelectionPattern]::Pattern).Current.GetSelection()
    if ($selection.Count -ne 1) { return $null }
    return $selection[0].Current.Name
}

Invoke-UITest -Name 'Sound Press again menu lists, selects, and saves every behavior' -Body {
    $config = New-DcsbConfig -PrimaryOutput 'Disabled'
    $config.PresetCollection.Add((New-DcsbPreset -Name 'Press again menu'))
    Save-DcsbConfigModel $config

    $process = Start-Dcsb
    $main = Get-DcsbMainWindow -ProcessId $process.Id
    Set-DcsbForeground $main

    $addButton = Find-ButtonByHelpText $main 'Add Sound'
    Assert-True ($null -ne $addButton) "'Add Sound' button not found in the main window"
    Invoke-UIElement $addButton
    $soundWindow = Wait-DcsbWindow $main 'Sound'
    $combo = Get-PressAgainCombo $soundWindow

    $items = @(Get-ComboItemNames $combo)
    Write-Host "  menu items: $($items -join ', ')"
    Assert-True ($items.Count -eq 3) 'Press again menu must contain exactly three choices'
    Assert-True ($items[0] -eq 'Restart / layer') 'first Press again choice must be Restart / layer'
    Assert-True ($items[1] -eq 'Stop') 'second Press again choice must be Stop'
    Assert-True ($items[2] -eq 'Pause / resume') 'third Press again choice must be Pause / resume'

    $selected = Get-SelectedComboItemName $combo
    Write-Host "  default selection: $selected"
    Assert-True ($selected -eq 'Pause / resume') 'new sounds must default to Pause / resume'

    # Exercise every item, not just the final persisted value.
    foreach ($choice in 'Stop', 'Restart / layer', 'Pause / resume') {
        Select-ComboItem -Combo $combo -ItemName $choice
        $selected = Get-SelectedComboItemName $combo
        Write-Host "  selected: $selected"
        Assert-True ($selected -eq $choice) "selecting '$choice' did not update the combo box"
    }

    # Leave Stop selected and verify the two-way binding reaches the saved config.
    Select-ComboItem -Combo $combo -ItemName 'Stop'
    $soundWindow.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern).Close()
    Start-Sleep -Seconds 2 # ConfigurationManager saves nested changes after 1 second.
    Stop-Dcsb $process

    $serializer = New-Object System.Xml.Serialization.XmlSerializer ([DCSB.Models.ConfigurationModel])
    $stream = [IO.File]::OpenRead($script:ConfigPath)
    try { $saved = $serializer.Deserialize($stream) } finally { $stream.Dispose() }
    $savedBehavior = $saved.PresetCollection[0].SoundCollection[0].PressAgainBehavior
    Write-Host "  saved behavior: $savedBehavior"
    Assert-True ($savedBehavior -eq [DCSB.Models.PressAgainBehavior]::Stop) `
        'selecting Stop in the menu must persist Stop to the sound configuration'
}
