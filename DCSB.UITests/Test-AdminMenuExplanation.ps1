# Verifies that Settings explains when and why elevated access is required and
# still offers the existing restart choice.
. "$PSScriptRoot\UITestHelpers.ps1"

Invoke-UITest -Name 'Settings explains elevated-game hotkey restrictions' -Body {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal $identity
    if ($principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
        throw 'SKIP: administrator-access settings are intentionally hidden when DCSB is already elevated'
    }

    New-DcsbTestConfig -PrimaryOutput 'Disabled'
    $process = Start-Dcsb
    try {
        $main = Get-DcsbMainWindow -ProcessId $process.Id
        Assert-True ($null -eq (Find-DescendantByName $main 'Why admin may be needed')) `
            'administrator explanation should no longer be in the main menu'
        $settings = Open-DcsbSettings $main
        Select-DcsbSettingsTab $settings 'Other'
        $button = Find-DescendantByName $settings 'Learn more or restart as administrator…'
        Assert-True ($null -ne $button) 'administrator explanation button was not found in Settings -> Other'
        Invoke-UIElement $button

        $dialog = Wait-DcsbModalDialog -MainWindow $main -TimeoutSec 5
        Assert-True ($null -ne $dialog) 'administrator explanation dialog did not appear'
        $text = Get-DialogText $dialog
        Assert-True ($text -like '*works with most games*') 'dialog does not explain that admin is normally unnecessary'
        Assert-True ($text -like '*global keyboard and mouse input*') 'dialog does not explain the Windows input restriction'
        Assert-True ($text -like '*game or launcher running as administrator*') 'dialog does not identify elevated games and launchers'
        Assert-True ($text -like '*Restart DCSB as administrator now?*') 'dialog does not offer the administrator restart'
        Assert-True (Invoke-DialogButton -Dialog $dialog -Name 'No') 'No button not found in administrator dialog'
        Close-DcsbSettings $settings
    } finally {
        Stop-Dcsb $process
    }
}
