# Drives the first-run setup wizard end to end:
#   - launches with a fresh (SetupCompleted=false) config and asserts the wizard
#     auto-opens on first run,
#   - step 1 detects the virtual cable, preferring plain "CABLE Input" over the
#     "16ch" variant,
#   - step 2 auto-configures the cable as the second output (first output is
#     switched to Disabled here so the test tone never plays on real speakers),
#   - step 3 plays the test tone and we meter the cable's render endpoint to prove
#     audio actually reached it, then read the wizard's own success verdict,
#   - Finish closes the wizard and marks setup complete in the saved config.
#
# Requires an interactive desktop, an active VB-Audio virtual cable, and pwsh 7+.
$ErrorActionPreference = 'Stop'
. "$PSScriptRoot\UITestHelpers.ps1"

Invoke-UITest -Name 'Setup wizard first-run flow' -Body {
    $cableRender = 'CABLE Input (VB-Audio Virtual Cable)'
    $cableCapture = 'CABLE Output (VB-Audio Virtual Cable)'

    $renderNames = Get-OutputDeviceNames
    if ($renderNames -notcontains $cableRender) {
        throw "SKIP: '$cableRender' not present; this test needs VB-Audio Virtual Cable installed."
    }

    # fresh install state: SetupCompleted explicitly false so the wizard auto-runs
    $config = New-DcsbConfig -PrimaryOutput $cableRender
    $config.SetupCompleted = $false
    $config.SetupCompletedSpecified = $true
    $preset = New-DcsbPreset -Name 'UITest'
    $config.PresetCollection.Add($preset)
    Save-DcsbConfigModel $config

    $process = Start-Dcsb
    try {
        $main = Get-DcsbMainWindow -ProcessId $process.Id

        # 1. the wizard auto-opens on first run
        $wizard = Wait-DcsbWindow -MainWindow $main -Title 'DCSB Setup' -TimeoutSec 15
        Assert-True ($null -ne $wizard) 'setup wizard should auto-open on first run'
        Set-DcsbForeground $wizard

        # step 1: the plain CABLE Input must be detected, not the 16ch variant
        $texts = Get-DialogText $wizard
        Assert-True ($texts -like "*$cableRender*") "step 1 should show detected cable '$cableRender'; saw: $texts"
        Assert-True ($texts -notlike '*16ch*') "step 1 should prefer plain CABLE Input over the 16ch variant; saw: $texts"

        # advance to step 2
        $next = Find-DescendantByName $wizard 'Next'
        Assert-True ($null -ne $next) 'Next button should exist'
        Invoke-UIElement $next
        Start-Sleep -Milliseconds 600

        # step 2: three device combos; auto-config should have set the 2nd to the cable
        $comboCondition = New-Object System.Windows.Automation.PropertyCondition(
            [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
            [System.Windows.Automation.ControlType]::ComboBox)
        $combos = $wizard.FindAll([System.Windows.Automation.TreeScope]::Descendants, $comboCondition)
        Assert-True ($combos.Count -ge 3) "step 2 should have 3 device combos, found $($combos.Count)"

        # auto-config applies through the real device setters, which persist to config
        # (debounced ~1s) - the config is the reliable source of truth for the selection
        $deadline = (Get-Date).AddSeconds(5)
        $secondary = $null
        do {
            Start-Sleep -Milliseconds 400
            # the save replaces the file on disk, so it can be briefly absent/locked
            try { $raw = Get-Content $script:ConfigPath -Raw -ErrorAction Stop } catch { continue }
            if ($raw -match '<SecondaryOutput>([^<]*)</SecondaryOutput>') { $secondary = $matches[1] }
        } while ($secondary -ne $cableRender -and (Get-Date) -lt $deadline)
        Assert-True ($secondary -eq $cableRender) `
            "second output should be auto-set to '$cableRender', was '$secondary'"

        # first output -> Disabled so the tone never hits real speakers during the test
        Select-ComboItem -Combo $combos[0] -ItemName 'Disabled'

        # advance to step 3 and play the test tone
        Set-DcsbForeground $wizard
        Invoke-UIElement (Find-DescendantByName $wizard 'Next')
        Start-Sleep -Milliseconds 600

        $play = Find-DescendantByName $wizard 'Play test sound'
        Assert-True ($null -ne $play) 'Play test sound button should exist on step 3'
        Invoke-UIElement $play

        # meter the cable's render endpoint: proves the tone actually reached the cable
        $peak = Get-MaxPeak -DeviceName $cableRender -Seconds 2
        Assert-True ($peak -gt $script:PeakThreshold) `
            "test tone should reach '$cableRender' (peak $peak <= $($script:PeakThreshold))"

        # the wizard's own verify verdict should report success (it opened a capture on
        # CABLE Output and saw the tone come back out of the cable)
        Start-Sleep -Milliseconds 800
        $verdict = Get-DialogText $wizard
        Assert-True ($verdict -like '*Discord*') "step 3 verdict should confirm success; saw: $verdict"

        # step 4: cable-latency guidance (tip text always; the control panel button
        # only when VBCABLE_ControlPanel.exe exists on disk - true wherever VB-Cable
        # is installed, which this test already requires). The button is asserted,
        # not clicked: launching the panel triggers a UAC prompt.
        Set-DcsbForeground $wizard
        Invoke-UIElement (Find-DescendantByName $wizard 'Next')
        Start-Sleep -Milliseconds 500
        $step4Text = Get-DialogText $wizard
        Assert-True ($step4Text -like '*reduce the cable*') "step 4 should show the latency tip; saw: $step4Text"
        $controlPanelInstalled = (Test-Path 'C:\Program Files\VB\CABLE\VBCABLE_ControlPanel.exe') -or
                                 (Test-Path 'C:\Program Files (x86)\VB\CABLE\VBCABLE_ControlPanel.exe')
        $panelButton = Find-DescendantByName $wizard 'Open VB-Cable Control Panel...'
        if ($controlPanelInstalled) {
            Assert-True ($null -ne $panelButton) 'control panel button should be visible when VB-Cable is installed'
        } else {
            Assert-True ($null -eq $panelButton) 'control panel button should be hidden without VBCABLE_ControlPanel.exe'
        }

        $finish = Find-DescendantByName $wizard 'Finish'
        Assert-True ($null -ne $finish) 'Finish button should exist on the last step'
        Invoke-UIElement $finish
        Start-Sleep -Milliseconds 800
    } finally {
        Stop-Dcsb -Process $process
    }

    # completion must be persisted so the wizard never auto-shows again
    $saved = Get-Content $script:ConfigPath -Raw
    Assert-True ($saved -match '<SetupCompleted>true</SetupCompleted>') `
        'finishing the wizard should persist SetupCompleted=true'
}
