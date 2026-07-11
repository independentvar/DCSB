# Verifies that a non-elevated DCSB independently detects a stable elevated
# fullscreen foreground process, shows the existing restart-as-admin prompt,
# and does not show it again during the same session.
. "$PSScriptRoot\UITestHelpers.ps1"

Add-Type -AssemblyName System.Windows.Forms

$fakeGameScript = @'
Add-Type -AssemblyName System.Windows.Forms, System.Drawing
$form = New-Object System.Windows.Forms.Form
$form.FormBorderStyle = 'None'
$form.StartPosition = 'Manual'
$form.Bounds = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
$form.BackColor = [System.Drawing.Color]::Black
$form.TopMost = $true
$form.Text = 'DCSB elevated fake game'
$form.Add_Shown({ $form.Activate() })
[System.Windows.Forms.Application]::Run($form)
'@

function Test-CurrentProcessElevated {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    return (New-Object Security.Principal.WindowsPrincipal $identity).IsInRole(
        [Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Start-DcsbUnelevatedFromExplorer {
    $before = @(Get-Process DCSB -ErrorAction SilentlyContinue | ForEach-Object Id)
    Start-Process explorer.exe -ArgumentList "`"$script:ExePath`""

    $deadline = (Get-Date).AddSeconds(15)
    do {
        Start-Sleep -Milliseconds 500
        $process = Get-Process DCSB -ErrorAction SilentlyContinue |
            Where-Object { $_.Id -notin $before } |
            Sort-Object StartTime -Descending |
            Select-Object -First 1
        if ($process -and (Get-DcsbMainWindow -ProcessId $process.Id)) { return $process }
    } while ((Get-Date) -lt $deadline)

    throw 'SKIP: Explorer could not launch a separate non-elevated DCSB process'
}

Invoke-UITest -Name 'Elevated fullscreen app automatically offers an admin restart once' -Body {
    if (-not (Test-CurrentProcessElevated)) {
        throw 'SKIP: test runner must be elevated to create an elevated fake game and a non-elevated DCSB'
    }

    New-DcsbTestConfig -PrimaryOutput 'Disabled'
    $process = Start-DcsbUnelevatedFromExplorer
    $fakeGame = $null
    $scriptPath = Join-Path ([IO.Path]::GetTempPath()) 'dcsb-elevated-fake-game.ps1'
    try {
        $main = Get-DcsbMainWindow -ProcessId $process.Id
        Set-Content $scriptPath $fakeGameScript
        $shell = (Get-Process -Id $PID).Path
        $fakeGame = Start-Process $shell -ArgumentList '-NoProfile', '-ExecutionPolicy', 'Bypass',
            '-WindowStyle', 'Hidden', '-File', $scriptPath -PassThru

        $deadline = (Get-Date).AddSeconds(10)
        while (-not [DCSB.Utils.FullscreenDetector]::IsElevatedFullscreenAppForeground()) {
            if ((Get-Date) -gt $deadline) { throw 'SKIP: elevated fake game never became fullscreen foreground' }
            Start-Sleep -Milliseconds 400
            $fakeGame.Refresh()
            if ($fakeGame.MainWindowHandle -ne [IntPtr]::Zero) {
                [DcsbUiTest.Native]::SetForegroundWindow($fakeGame.MainWindowHandle) | Out-Null
            }
        }

        $overlay = Wait-DcsbWindow -MainWindow $main -Title 'DCSB Overlay' -TimeoutSec 3
        $overlayText = Get-DialogText $overlay
        Assert-True ($overlayText -like '*This game is running as administrator*') `
            'in-game administrator warning did not appear before the restart prompt'

        $dialog = Wait-DcsbModalDialog -MainWindow $main -TimeoutSec 10
        Assert-True ($null -ne $dialog) 'restart-as-admin prompt did not appear'
        Assert-True ((Get-DialogText $dialog) -like '*Restart DCSB as administrator now?*') `
            'unexpected automatic prompt text'
        Assert-True (Invoke-DialogButton -Dialog $dialog -Name 'No') 'No button not found in admin prompt'

        [DcsbUiTest.Native]::SetForegroundWindow($fakeGame.MainWindowHandle) | Out-Null
        Start-Sleep -Seconds 4
        Assert-True ($null -eq (Get-DcsbModalDialog $main)) `
            'restart-as-admin prompt appeared more than once in one session'
    } finally {
        if ($fakeGame -and -not $fakeGame.HasExited) { Stop-Process -Id $fakeGame.Id -Force }
        Remove-Item $scriptPath -Force -ErrorAction SilentlyContinue
        Stop-Dcsb $process
    }
}
