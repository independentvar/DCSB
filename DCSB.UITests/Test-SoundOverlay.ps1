# Verifies the in-game sound overlay: while a borderless-fullscreen window from
# another process is in the foreground, DCSB shows a topmost click-through bar
# at the top of that monitor listing the sounds and their keys, and hides it
# again when the fullscreen window goes away.
. "$PSScriptRoot\UITestHelpers.ps1"

Add-Type -AssemblyName System.Windows.Forms

if (-not ('DcsbUiTest.OverlayNative' -as [type])) {
    Add-Type @'
using System;
using System.Runtime.InteropServices;
using System.Text;
namespace DcsbUiTest {
    public static class OverlayNative {
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern IntPtr FindWindowW(string className, string windowName);
        [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hWnd);
        [DllImport("user32.dll")] public static extern int GetWindowLong(IntPtr hWnd, int index);
        [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
        [DllImport("user32.dll")] public static extern IntPtr GetWindow(IntPtr hWnd, uint cmd);
        [DllImport("user32.dll")] public static extern IntPtr GetTopWindow(IntPtr hWnd);
        [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out int pid);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern int GetWindowTextW(IntPtr hWnd, StringBuilder text, int maxCount);
        [StructLayout(LayoutKind.Sequential)]
        public struct RECT { public int Left, Top, Right, Bottom; }
    }
}
'@
}

function Get-OverlayWindowHandle {
    # [NullString]::Value: PowerShell binds $null to string parameters as "",
    # which FindWindow would treat as an (unmatchable) class name
    return [DcsbUiTest.OverlayNative]::FindWindowW([NullString]::Value, 'DCSB Overlay')
}

function Write-DcsbWindowDump {
    param([int]$ProcessId)
    $process = Get-Process -Id $ProcessId -ErrorAction SilentlyContinue
    Write-Host "  [diag] DCSB process alive: $([bool]$process)"
    $handleRef = Get-OverlayWindowHandle
    Write-Host "  [diag] FindWindow('DCSB Overlay') = 0x$($handleRef.ToInt64().ToString('X'))"
    $current = [DcsbUiTest.OverlayNative]::GetTopWindow([IntPtr]::Zero)
    $count = 0
    while ($current -ne [IntPtr]::Zero -and $count -lt 2000) {
        $pid2 = 0
        [DcsbUiTest.OverlayNative]::GetWindowThreadProcessId($current, [ref]$pid2) | Out-Null
        if ($pid2 -eq $ProcessId) {
            $text = New-Object System.Text.StringBuilder 256
            [DcsbUiTest.OverlayNative]::GetWindowTextW($current, $text, 256) | Out-Null
            $rect = New-Object DcsbUiTest.OverlayNative+RECT
            [DcsbUiTest.OverlayNative]::GetWindowRect($current, [ref]$rect) | Out-Null
            $visible = [DcsbUiTest.OverlayNative]::IsWindowVisible($current)
            Write-Host "  [diag] 0x$($current.ToInt64().ToString('X')) visible=$visible title='$($text.ToString())' rect=($($rect.Left),$($rect.Top))-($($rect.Right),$($rect.Bottom))"
        }
        $current = [DcsbUiTest.OverlayNative]::GetWindow($current, 2)
        $count++
    }
}

function Wait-OverlayVisible {
    param([bool]$Expected, [int]$TimeoutSec = 6)
    $deadline = (Get-Date).AddSeconds($TimeoutSec)
    do {
        $handle = Get-OverlayWindowHandle
        $visible = ($handle -ne [IntPtr]::Zero) -and [DcsbUiTest.OverlayNative]::IsWindowVisible($handle)
        if ($visible -eq $Expected) { return $true }
        Start-Sleep -Milliseconds 400
    } while ((Get-Date) -lt $deadline)
    return $false
}

# a black borderless window spanning the primary monitor in a separate process,
# standing in for a borderless-fullscreen game
$fakeGameScript = @'
Add-Type -AssemblyName System.Windows.Forms, System.Drawing
$form = New-Object System.Windows.Forms.Form
$form.FormBorderStyle = 'None'
$form.StartPosition = 'Manual'
$form.Bounds = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
$form.BackColor = [System.Drawing.Color]::Black
$form.TopMost = $true
$form.Add_Shown({ $form.Activate() })
[System.Windows.Forms.Application]::Run($form)
'@

Invoke-UITest -Name 'Sound overlay appears over a fullscreen app and hides afterwards' -Body {
    $deviceNames = @(Get-OutputDeviceNames)
    $primary = if ($deviceNames.Count) { $deviceNames[0] } else { 'Disabled' }
    New-DcsbTestConfig -PrimaryOutput $primary

    # the overlay only lists sounds that have keys bound
    [xml]$configXml = Get-Content $script:ConfigPath
    $keys = $configXml.CreateElement('Keys')
    $key = $configXml.CreateElement('VKey')
    $key.InnerText = 'KEY_1'
    $keys.AppendChild($key) | Out-Null
    $configXml.ConfigurationModel.PresetCollection.Preset.SoundCollection.Sound.AppendChild($keys) | Out-Null
    $configXml.Save($script:ConfigPath)

    $process = Start-Dcsb
    $fakeGame = $null
    try {
        Start-Sleep -Seconds 3
        Assert-True (-not (Wait-OverlayVisible -Expected $true -TimeoutSec 3)) `
            'overlay is visible with no fullscreen app in the foreground'
        Write-Host '  overlay hidden while no fullscreen app is running'

        $scriptPath = Join-Path ([IO.Path]::GetTempPath()) 'dcsb-fake-game.ps1'
        Set-Content $scriptPath $fakeGameScript
        $shell = (Get-Process -Id $PID).Path
        # hidden console so only the borderless form competes for the foreground
        $fakeGame = Start-Process $shell -ArgumentList '-NoProfile', '-ExecutionPolicy', 'Bypass', '-WindowStyle', 'Hidden', '-File', $scriptPath -PassThru

        # precondition, not the feature under test: the fake game must actually
        # be detected as a foreground fullscreen app from this process too;
        # foreground assignment to a fresh process can be refused, so retry by
        # re-activating the form
        $left = 0; $top = 0; $width = 0; $height = 0
        $deadline = (Get-Date).AddSeconds(10)
        while (-not [DCSB.Utils.FullscreenDetector]::TryGetFullscreenAppBounds([ref]$left, [ref]$top, [ref]$width, [ref]$height)) {
            if ((Get-Date) -gt $deadline) { throw 'SKIP: fake game window never became the foreground fullscreen app' }
            Start-Sleep -Milliseconds 500
            $fakeGame.Refresh()
            if ($fakeGame.MainWindowHandle -ne [IntPtr]::Zero) {
                [DcsbUiTest.Native]::SetForegroundWindow($fakeGame.MainWindowHandle) | Out-Null
            }
        }
        Write-Host "  fake fullscreen game is in the foreground ($left,$top ${width}x$height)"

        if (-not (Wait-OverlayVisible -Expected $true -TimeoutSec 10)) {
            Write-DcsbWindowDump -ProcessId $process.Id
            throw 'Assertion failed: overlay did not appear within 10 seconds of a fullscreen app taking the foreground'
        }
        Write-Host '  overlay appeared over the fullscreen app'

        $handle = Get-OverlayWindowHandle
        $rect = New-Object DcsbUiTest.OverlayNative+RECT
        [DcsbUiTest.OverlayNative]::GetWindowRect($handle, [ref]$rect) | Out-Null
        $screen = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
        Assert-True ($rect.Top -eq $screen.Top -and $rect.Left -eq $screen.Left -and $rect.Right -eq $screen.Right) `
            "overlay rect ($($rect.Left),$($rect.Top))-($($rect.Right),$($rect.Bottom)) does not span the top of the screen $screen"
        Write-Host "  overlay spans the top of the screen: ($($rect.Left),$($rect.Top))-($($rect.Right),$($rect.Bottom))"

        # WS_EX_TRANSPARENT (0x20): clicks fall through to the game
        $exStyle = [DcsbUiTest.OverlayNative]::GetWindowLong($handle, -20)
        Assert-True (($exStyle -band 0x20) -ne 0) 'overlay window is not click-through (WS_EX_TRANSPARENT missing)'
        Write-Host '  overlay is click-through'

        # z-order: walking down from the top of the z-order must meet the overlay
        # before the fake game window (GetWindow GW_HWNDNEXT = 2)
        $gameHandle = $fakeGame.MainWindowHandle
        $current = [DcsbUiTest.OverlayNative]::GetTopWindow([IntPtr]::Zero)
        $overlayAboveGame = $false
        while ($current -ne [IntPtr]::Zero) {
            if ($current -eq $handle) { $overlayAboveGame = $true; break }
            if ($current -eq $gameHandle) { break }
            $current = [DcsbUiTest.OverlayNative]::GetWindow($current, 2)
        }
        Assert-True $overlayAboveGame 'overlay window is below the fullscreen app in the z-order'
        Write-Host '  overlay is above the fullscreen app in the z-order'

        Stop-Process -Id $fakeGame.Id -Force
        $fakeGame = $null
        Assert-True (Wait-OverlayVisible -Expected $false -TimeoutSec 10) `
            'overlay did not hide within 10 seconds of the fullscreen app closing'
        Write-Host '  overlay hidden again after the fullscreen app closed'
    } finally {
        if ($fakeGame -and -not $fakeGame.HasExited) { Stop-Process -Id $fakeGame.Id -Force }
    }

    Stop-Dcsb $process
}
