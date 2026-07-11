# Verifies the per-sound repeated-hotkey behavior through Raw Input and the real
# playback engine. Pause must hold the decoder position and resume from it; Stop
# must clear playback so the following press starts the sound from the beginning.
. "$PSScriptRoot\UITestHelpers.ps1"

function New-LongToneFile {
    param([Parameter(Mandatory)] [string]$Path)

    if (-not ('DCSB.Tests.TestToneFile' -as [type])) {
        Add-Type -Path (Join-Path $script:RepoRoot 'DCSB.Tests\TestToneFile.cs')
    }

    [DCSB.Tests.TestToneFile]::Create($Path, 10)
}

function Send-SoundHotkey {
    param([Parameter(Mandatory)] $MainWindow)
    Set-DcsbForeground $MainWindow
    [DcsbUiTest.Native]::keybd_event(0x77, 0, 0, [UIntPtr]::Zero) # F8 down
    Start-Sleep -Milliseconds 80
    [DcsbUiTest.Native]::keybd_event(0x77, 0, 2, [UIntPtr]::Zero) # F8 up
    Start-Sleep -Milliseconds 250
}

function Get-SeekSlider {
    param([Parameter(Mandatory)] $MainWindow)
    $condition = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
        [System.Windows.Automation.ControlType]::Slider)
    foreach ($slider in $MainWindow.FindAll([System.Windows.Automation.TreeScope]::Descendants, $condition)) {
        $range = $null
        if (-not $slider.TryGetCurrentPattern([System.Windows.Automation.RangeValuePattern]::Pattern, [ref]$range)) { continue }
        # The other main-window slider is master volume with a maximum of 100.
        if ($range.Current.Maximum -gt 0 -and $range.Current.Maximum -lt 30) { return $slider }
    }
    return $null
}

function Wait-SeekPosition {
    param(
        [Parameter(Mandatory)] $MainWindow,
        [double]$Minimum,
        [int]$TimeoutSec = 5
    )
    $deadline = (Get-Date).AddSeconds($TimeoutSec)
    do {
        $slider = Get-SeekSlider $MainWindow
        if ($slider) {
            $value = $slider.GetCurrentPattern([System.Windows.Automation.RangeValuePattern]::Pattern).Current.Value
            if ($value -ge $Minimum) { return $slider }
        }
        Start-Sleep -Milliseconds 100
    } while ((Get-Date) -lt $deadline)
    throw "Seek position did not reach $Minimum seconds within $TimeoutSec seconds."
}

function Save-HotkeyBehaviorConfig {
    param(
        [Parameter(Mandatory)] [string]$Device,
        [Parameter(Mandatory)] [string]$SoundFile,
        [Parameter(Mandatory)] [DCSB.Models.PressAgainBehavior]$Behavior
    )
    $config = New-DcsbConfig -PrimaryOutput $Device
    $preset = New-DcsbPreset -Name 'Hotkey behavior'
    $sound = New-DcsbSound -Name $script:TestSoundName -File $SoundFile
    $sound.Keys.Add([DCSB.Utils.VKey]::F8)
    $sound.PressAgainBehavior = $Behavior
    $preset.SoundCollection.Add($sound)
    $config.PresetCollection.Add($preset)
    Save-DcsbConfigModel $config
}

Invoke-UITest -Name 'Repeated sound hotkey pauses/resumes or stops as configured' -Body {
    $deviceNames = Get-OutputDeviceNames
    Assert-True ($deviceNames.Count -gt 0) 'at least one active output device is required'
    $device = $deviceNames[0]
    $tone = Join-Path ([IO.Path]::GetTempPath()) ("dcsb-hotkey-tone-" + [Guid]::NewGuid().ToString('N') + '.wav')
    New-LongToneFile -Path $tone

    try {
        # Pause / resume is the default, but set it explicitly so this phase tests
        # the serialized UI-facing choice as well as the model default unit test.
        Save-HotkeyBehaviorConfig -Device $device -SoundFile $tone -Behavior ([DCSB.Models.PressAgainBehavior]::Pause)
        $process = Start-Dcsb
        $main = Get-DcsbMainWindow -ProcessId $process.Id

        Send-SoundHotkey $main
        $seek = Wait-SeekPosition -MainWindow $main -Minimum 1.0
        Send-SoundHotkey $main
        $pausedAt = $seek.GetCurrentPattern([System.Windows.Automation.RangeValuePattern]::Pattern).Current.Value
        Start-Sleep -Seconds 1
        $stillPausedAt = $seek.GetCurrentPattern([System.Windows.Automation.RangeValuePattern]::Pattern).Current.Value
        Write-Host "  paused position: $([math]::Round($pausedAt, 2)) -> $([math]::Round($stillPausedAt, 2))"
        Assert-True ([math]::Abs($stillPausedAt - $pausedAt) -lt 0.35) 'second F8 press must pause without advancing playback'

        Send-SoundHotkey $main
        Start-Sleep -Seconds 1
        $resumedAt = $seek.GetCurrentPattern([System.Windows.Automation.RangeValuePattern]::Pattern).Current.Value
        Write-Host "  resumed position: $([math]::Round($resumedAt, 2))"
        Assert-True ($resumedAt -gt ($pausedAt + 0.6)) 'third F8 press must resume from the paused position instead of restarting'
        Stop-Dcsb $process

        # Stop clears the tracked sound and seekbar; another press starts it anew.
        Save-HotkeyBehaviorConfig -Device $device -SoundFile $tone -Behavior ([DCSB.Models.PressAgainBehavior]::Stop)
        $process = Start-Dcsb
        $main = Get-DcsbMainWindow -ProcessId $process.Id
        Send-SoundHotkey $main
        $seek = Wait-SeekPosition -MainWindow $main -Minimum 0.7
        Send-SoundHotkey $main
        Start-Sleep -Milliseconds 400
        $stoppedAt = $seek.GetCurrentPattern([System.Windows.Automation.RangeValuePattern]::Pattern).Current.Value
        Write-Host "  stopped position: $([math]::Round($stoppedAt, 2))"
        Assert-True ($stoppedAt -lt 0.1) 'second F8 press in Stop mode must reset playback position'

        Send-SoundHotkey $main
        $null = Wait-SeekPosition -MainWindow $main -Minimum 0.5
        Stop-Dcsb $process
    } finally {
        Remove-Item $tone -Force -ErrorAction SilentlyContinue
    }
}
