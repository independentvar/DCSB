# Shared helpers for DCSB UI/integration tests. Dot-source from a test script:
#   . "$PSScriptRoot\UITestHelpers.ps1"
# Requires an interactive Windows desktop session and at least one active
# audio output device. Compatible with Windows PowerShell 5.1 and PowerShell 7.

$script:RepoRoot = Split-Path $PSScriptRoot -Parent
$script:BinDir = Join-Path $script:RepoRoot 'DCSB\bin\Release'
$script:ExePath = Join-Path $script:BinDir 'DCSB.exe'
$script:ConfigDir = Join-Path $env:ProgramData 'DCSB'
$script:ConfigPath = Join-Path $script:ConfigDir 'config.xml'

$script:TestSoundName = 'uitest-sound'
$script:PeakThreshold = 0.01

function Initialize-UITestContext {
    if (-not (Test-Path $script:ExePath)) {
        throw "Release build not found at $script:ExePath - run 'msbuild DCSB.sln /p:Configuration=Release' first."
    }

    Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, System.Drawing

    # the app's own assemblies give us schema-correct config serialization and
    # WASAPI device access without duplicating any of that logic in the tests
    foreach ($dll in 'CommonServiceLocator.dll', 'GalaSoft.MvvmLight.dll', 'DCSB.Utils.dll', 'DCSB.Models.dll', 'NAudio.dll') {
        $path = Join-Path $script:BinDir $dll
        if (Test-Path $path) { Add-Type -Path $path }
    }

    if (-not ('DcsbUiTest.Native' -as [type])) {
        Add-Type @'
using System;
using System.Runtime.InteropServices;
namespace DcsbUiTest {
    public static class Native {
        [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
        [DllImport("user32.dll")] public static extern void mouse_event(uint flags, uint dx, uint dy, uint data, UIntPtr extra);
        [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
    }
}
'@
    }
}

# ---------- audio devices ----------

function Get-OutputDeviceNames {
    $enumerator = New-Object NAudio.CoreAudioApi.MMDeviceEnumerator
    try {
        $endpoints = $enumerator.EnumerateAudioEndPoints([NAudio.CoreAudioApi.DataFlow]::Render, [NAudio.CoreAudioApi.DeviceState]::Active)
        $names = @()
        foreach ($endpoint in $endpoints) { $names += $endpoint.FriendlyName }
        return $names
    } finally {
        $enumerator.Dispose()
    }
}

function Get-MaxPeak {
    param(
        [Parameter(Mandatory)] [string]$DeviceName,
        [double]$Seconds = 3
    )
    $enumerator = New-Object NAudio.CoreAudioApi.MMDeviceEnumerator
    try {
        $endpoints = $enumerator.EnumerateAudioEndPoints([NAudio.CoreAudioApi.DataFlow]::Render, [NAudio.CoreAudioApi.DeviceState]::Active)
        $device = $endpoints | Where-Object { $_.FriendlyName -eq $DeviceName } | Select-Object -First 1
        if (-not $device) { throw "Output device '$DeviceName' not found for metering." }
        $max = 0.0
        $iterations = [int]($Seconds * 10)
        for ($i = 0; $i -lt $iterations; $i++) {
            $value = $device.AudioMeterInformation.MasterPeakValue
            if ($value -gt $max) { $max = $value }
            Start-Sleep -Milliseconds 100
        }
        return $max
    } finally {
        $enumerator.Dispose()
    }
}

function Get-TestWavFile {
    $preferred = Join-Path $env:windir 'Media\Alarm01.wav'
    if (Test-Path $preferred) { return $preferred }
    $any = Get-ChildItem (Join-Path $env:windir 'Media') -Filter *.wav | Select-Object -First 1
    if (-not $any) { throw 'No .wav file found under %windir%\Media to use as a test sound.' }
    return $any.FullName
}

# ---------- config ----------

function Backup-DcsbConfig {
    $backup = Join-Path ([IO.Path]::GetTempPath()) ("dcsb-config-backup-" + [Guid]::NewGuid().ToString('N') + ".xml")
    if (Test-Path $script:ConfigPath) {
        Copy-Item $script:ConfigPath $backup -Force
        return $backup
    }
    return $null
}

function Restore-DcsbConfig {
    param([string]$BackupPath)
    if ($BackupPath -and (Test-Path $BackupPath)) {
        Copy-Item $BackupPath $script:ConfigPath -Force
        Remove-Item $BackupPath -Force
    } elseif (Test-Path $script:ConfigPath) {
        # there was no config before the tests ran
        Remove-Item $script:ConfigPath -Force
    }
}

function New-DcsbTestConfig {
    param(
        [Parameter(Mandatory)] [string]$PrimaryOutput,
        [string]$SoundFile
    )
    if (-not $SoundFile) { $SoundFile = Get-TestWavFile }

    $config = New-Object DCSB.Models.ConfigurationModel
    $config.PrimaryOutput = $PrimaryOutput
    $config.SecondaryOutput = 'Disabled'
    $config.Volume = 100
    $config.PrimaryDeviceVolume = 100
    $config.WindowWidth = 900
    $config.WindowHeight = 500

    $preset = New-Object DCSB.Models.Preset
    $preset.Name = 'UITest'
    $sound = New-Object DCSB.Models.Sound
    $sound.Name = $script:TestSoundName
    $sound.Volume = 100
    $sound.Files.Add($SoundFile)
    $preset.SoundCollection.Add($sound)
    $config.PresetCollection.Add($preset)

    if (-not (Test-Path $script:ConfigDir)) { New-Item -ItemType Directory -Path $script:ConfigDir | Out-Null }
    $serializer = New-Object System.Xml.Serialization.XmlSerializer ([DCSB.Models.ConfigurationModel])
    $stream = [IO.File]::Create($script:ConfigPath)
    try { $serializer.Serialize($stream, $config) } finally { $stream.Dispose() }
}

function Get-ConfigPrimaryOutput {
    $content = Get-Content $script:ConfigPath -Raw
    if ($content -match '<PrimaryOutput>([^<]*)</PrimaryOutput>') { return $matches[1] }
    return $null
}

function Wait-ConfigPrimaryOutput {
    param(
        [Parameter(Mandatory)] [string]$Expected,
        [int]$TimeoutSec = 10
    )
    $deadline = (Get-Date).AddSeconds($TimeoutSec)
    do {
        if ((Get-ConfigPrimaryOutput) -eq $Expected) { return $true }
        Start-Sleep -Milliseconds 500
    } while ((Get-Date) -lt $deadline)
    return $false
}

# ---------- app lifecycle ----------

function Stop-AllDcsb {
    foreach ($process in Get-Process DCSB -ErrorAction SilentlyContinue) {
        $process.CloseMainWindow() | Out-Null
        if (-not $process.WaitForExit(3000)) {
            Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
            $process.WaitForExit(3000) | Out-Null
        }
    }
}

function Start-Dcsb {
    $process = Start-Process $script:ExePath -PassThru
    $window = $null
    $deadline = (Get-Date).AddSeconds(15)
    while ((Get-Date) -lt $deadline -and -not $window) {
        Start-Sleep -Milliseconds 500
        $process.Refresh()
        if ($process.HasExited) { throw "DCSB exited during startup (exit code $($process.ExitCode))." }
        $window = Get-DcsbMainWindow -ProcessId $process.Id
    }
    if (-not $window) { throw 'DCSB main window did not appear within 15 seconds.' }
    return $process
}

function Stop-Dcsb {
    param([Parameter(Mandatory)] $Process)
    if (-not $Process.HasExited) {
        $Process.CloseMainWindow() | Out-Null
        if (-not $Process.WaitForExit(3000)) {
            Stop-Process -Id $Process.Id -Force -ErrorAction SilentlyContinue
        }
    }
}

# ---------- UI automation ----------

function Get-DcsbMainWindow {
    param([Parameter(Mandatory)] [int]$ProcessId)
    $root = [System.Windows.Automation.AutomationElement]::RootElement
    $condition = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::ProcessIdProperty, $ProcessId)
    return $root.FindFirst([System.Windows.Automation.TreeScope]::Children, $condition)
}

function Find-DescendantByName {
    param(
        [Parameter(Mandatory)] $Element,
        [Parameter(Mandatory)] [string]$Name
    )
    $condition = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::NameProperty, $Name)
    return $Element.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $condition)
}

function Set-DcsbForeground {
    param([Parameter(Mandatory)] $Window)
    [DcsbUiTest.Native]::SetForegroundWindow([IntPtr]$Window.Current.NativeWindowHandle) | Out-Null
    Start-Sleep -Milliseconds 300
}

function Invoke-DoubleClickOn {
    param([Parameter(Mandatory)] $Element)
    $rect = $Element.Current.BoundingRectangle
    $x = [int]($rect.X + $rect.Width / 2)
    $y = [int]($rect.Y + $rect.Height / 2)
    [DcsbUiTest.Native]::SetCursorPos($x, $y) | Out-Null
    foreach ($i in 1..2) {
        [DcsbUiTest.Native]::mouse_event(2, 0, 0, 0, [UIntPtr]::Zero)  # LEFTDOWN
        [DcsbUiTest.Native]::mouse_event(4, 0, 0, 0, [UIntPtr]::Zero)  # LEFTUP
        Start-Sleep -Milliseconds 60
    }
}

function Invoke-PlayTestSound {
    param([Parameter(Mandatory)] $MainWindow)
    Set-DcsbForeground $MainWindow
    $row = Find-DescendantByName $MainWindow $script:TestSoundName
    if (-not $row) { throw "Sound row '$($script:TestSoundName)' not found in the main window." }
    Invoke-DoubleClickOn $row
}

# The settings window is an owned (modal) window of the main window; while it is
# open, clicks on the main window are ignored - close it before playing sounds.
function Open-DcsbSettings {
    param([Parameter(Mandatory)] $MainWindow)
    $menu = Find-DescendantByName $MainWindow 'Settings'
    if (-not $menu) { throw "'Settings' menu item not found." }
    $menu.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()

    $deadline = (Get-Date).AddSeconds(5)
    while ((Get-Date) -lt $deadline) {
        Start-Sleep -Milliseconds 400
        $windowCondition = New-Object System.Windows.Automation.PropertyCondition(
            [System.Windows.Automation.AutomationElement]::NameProperty, 'Settings')
        $candidates = $MainWindow.FindAll([System.Windows.Automation.TreeScope]::Descendants, $windowCondition)
        foreach ($candidate in $candidates) {
            if ($candidate.Current.ControlType.ProgrammaticName -eq 'ControlType.Window') { return $candidate }
        }
    }
    throw 'Settings window did not open within 5 seconds.'
}

function Close-DcsbSettings {
    param([Parameter(Mandatory)] $SettingsWindow)
    $SettingsWindow.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern).Close()
    Start-Sleep -Milliseconds 500
}

function Select-DcsbSettingsTab {
    param(
        [Parameter(Mandatory)] $SettingsWindow,
        [Parameter(Mandatory)] [string]$TabName
    )
    $tab = Find-DescendantByName $SettingsWindow $TabName
    if (-not $tab) { throw "Settings tab '$TabName' not found." }
    $tab.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
    Start-Sleep -Milliseconds 500
}

function Get-OutputDeviceCombos {
    param([Parameter(Mandatory)] $SettingsWindow)
    Select-DcsbSettingsTab $SettingsWindow 'Sound'
    $comboCondition = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
        [System.Windows.Automation.ControlType]::ComboBox)
    $combos = $SettingsWindow.FindAll([System.Windows.Automation.TreeScope]::Descendants, $comboCondition)
    if ($combos.Count -lt 2) { throw "Expected 2 output device combo boxes on the Sound tab, found $($combos.Count)." }
    return @($combos[0], $combos[1])  # first, second output
}

function Get-ComboItemNames {
    param([Parameter(Mandatory)] $Combo)
    $expand = $Combo.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern)
    $expand.Expand()
    Start-Sleep -Milliseconds 400
    try {
        $itemCondition = New-Object System.Windows.Automation.PropertyCondition(
            [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
            [System.Windows.Automation.ControlType]::ListItem)
        $items = $Combo.FindAll([System.Windows.Automation.TreeScope]::Descendants, $itemCondition)
        $names = @()
        foreach ($item in $items) { $names += $item.Current.Name }
        return $names
    } finally {
        $expand.Collapse()
        Start-Sleep -Milliseconds 200
    }
}

function Select-ComboItem {
    param(
        [Parameter(Mandatory)] $Combo,
        [Parameter(Mandatory)] [string]$ItemName
    )
    $expand = $Combo.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern)
    $expand.Expand()
    Start-Sleep -Milliseconds 300
    $item = Find-DescendantByName $Combo $ItemName
    if (-not $item) { $expand.Collapse(); throw "Combo item '$ItemName' not found." }
    $item.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
    $expand.Collapse()
    Start-Sleep -Milliseconds 400
}

# ---------- dialog windows and file dialogs ----------

function Invoke-UIElement {
    param([Parameter(Mandatory)] $Element)
    $Element.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
}

function Find-ButtonByHelpText {
    # WPF exposes a control's ToolTip as UIA HelpText; the icon-only toolbar
    # buttons have no Name, so the tooltip is the only stable way to find them
    param(
        [Parameter(Mandatory)] $Element,
        [Parameter(Mandatory)] [string]$HelpText
    )
    $condition = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::HelpTextProperty, $HelpText)
    return $Element.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $condition)
}

function Wait-DcsbWindow {
    # waits for a window with the given title belonging to the DCSB process
    param(
        [Parameter(Mandatory)] $MainWindow,
        [Parameter(Mandatory)] [string]$Title,
        [int]$TimeoutSec = 10
    )
    $processId = $MainWindow.Current.ProcessId
    $nameCondition = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::NameProperty, $Title)
    $deadline = (Get-Date).AddSeconds($TimeoutSec)
    while ((Get-Date) -lt $deadline) {
        Start-Sleep -Milliseconds 400
        # WPF owned windows show up as descendants of their owner ...
        $candidates = $MainWindow.FindAll([System.Windows.Automation.TreeScope]::Descendants, $nameCondition)
        foreach ($candidate in $candidates) {
            if ($candidate.Current.ControlType.ProgrammaticName -eq 'ControlType.Window') { return $candidate }
        }
        # ... while native common dialogs may only appear as top-level windows
        $candidates = [System.Windows.Automation.AutomationElement]::RootElement.FindAll(
            [System.Windows.Automation.TreeScope]::Children, $nameCondition)
        foreach ($candidate in $candidates) {
            if ($candidate.Current.ProcessId -eq $processId -and
                $candidate.Current.ControlType.ProgrammaticName -eq 'ControlType.Window') { return $candidate }
        }
    }
    throw "Window '$Title' did not appear within $TimeoutSec seconds."
}

function Select-OpenFileDialogFile {
    # types a full path into the common file dialog's file-name box and confirms.
    # Uses the dialog's locale-independent automation ids: 1148 = file-name
    # combo box, 1 = the Open/confirm button.
    param(
        [Parameter(Mandatory)] $FileDialog,
        [Parameter(Mandatory)] [string]$Path
    )
    $boxCondition = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::AutomationIdProperty, '1148')
    $fileNameBox = $FileDialog.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $boxCondition)
    if (-not $fileNameBox) { throw 'File-name box (automation id 1148) not found in the file dialog.' }

    # set the text on the Edit inside the combo box - setting the combo box's
    # own value pattern is silently ignored by the dialog, which then opens
    # whatever file it had preselected from the last-used folder
    $editCondition = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
        [System.Windows.Automation.ControlType]::Edit)
    $target = $fileNameBox.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $editCondition)
    if (-not $target) { $target = $fileNameBox }
    $valuePattern = $null
    if (-not $target.TryGetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern, [ref]$valuePattern)) {
        throw 'File-name box does not expose a value pattern.'
    }
    $valuePattern.SetValue($Path)
    Start-Sleep -Milliseconds 300
    if ($valuePattern.Current.Value -ne $Path) {
        throw "File-name box shows '$($valuePattern.Current.Value)' after typing '$Path'."
    }

    # require ControlType Button: the items in the dialog's file list also get
    # automation ids '1', '2', ... and would match (and get invoked!) otherwise
    $openCondition = New-Object System.Windows.Automation.AndCondition(
        (New-Object System.Windows.Automation.PropertyCondition(
            [System.Windows.Automation.AutomationElement]::AutomationIdProperty, '1')),
        (New-Object System.Windows.Automation.PropertyCondition(
            [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
            [System.Windows.Automation.ControlType]::Button)))
    $openButton = $FileDialog.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $openCondition)
    if (-not $openButton) { throw 'Open button (automation id 1) not found in the file dialog.' }
    Invoke-UIElement $openButton
    Start-Sleep -Milliseconds 500
}

function Get-WindowEdits {
    param([Parameter(Mandatory)] $Window)
    $editCondition = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
        [System.Windows.Automation.ControlType]::Edit)
    return $Window.FindAll([System.Windows.Automation.TreeScope]::Descendants, $editCondition)
}

function Get-TopEditValue {
    # the Name text box is the topmost text box in both the Sound and the
    # Counter dialog; neither has an automation id, so locate it by position
    param([Parameter(Mandatory)] $Window)
    $top = $null
    foreach ($edit in Get-WindowEdits $Window) {
        if (-not $top -or $edit.Current.BoundingRectangle.Y -lt $top.Current.BoundingRectangle.Y) { $top = $edit }
    }
    if (-not $top) { throw 'No text boxes found in the window.' }
    return $top.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).Current.Value
}

function Wait-TopEditValue {
    param(
        [Parameter(Mandatory)] $Window,
        [Parameter(Mandatory)] [string]$Expected,
        [int]$TimeoutSec = 5
    )
    $deadline = (Get-Date).AddSeconds($TimeoutSec)
    do {
        if ((Get-TopEditValue $Window) -eq $Expected) { return $true }
        Start-Sleep -Milliseconds 300
    } while ((Get-Date) -lt $deadline)
    return $false
}

function Wait-AnyEditContains {
    # waits until any text box in the window contains the given text (used to
    # confirm the dialog picked up a selected file before asserting on the name)
    param(
        [Parameter(Mandatory)] $Window,
        [Parameter(Mandatory)] [string]$Text,
        [int]$TimeoutSec = 5
    )
    $deadline = (Get-Date).AddSeconds($TimeoutSec)
    do {
        foreach ($edit in Get-WindowEdits $Window) {
            $valuePattern = $null
            if ($edit.TryGetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern, [ref]$valuePattern) -and
                $valuePattern.Current.Value -like "*$Text*") { return $true }
        }
        Start-Sleep -Milliseconds 300
    } while ((Get-Date) -lt $deadline)
    return $false
}

# ---------- assertions / result reporting ----------

function Assert-True {
    param(
        [Parameter(Mandatory)] [bool]$Condition,
        [Parameter(Mandatory)] [string]$Message
    )
    if (-not $Condition) { throw "Assertion failed: $Message" }
}

# Standard wrapper: runs the test body with config backup/restore and app
# teardown, prints PASS/FAIL/SKIP and exits 0/1/2 (runner relies on these codes).
function Invoke-UITest {
    param(
        [Parameter(Mandatory)] [string]$Name,
        [Parameter(Mandatory)] [scriptblock]$Body
    )
    $exitCode = 1
    $backup = $null
    try {
        Initialize-UITestContext
        Stop-AllDcsb
        $backup = Backup-DcsbConfig
        & $Body
        Write-Host "PASS: $Name"
        $exitCode = 0
    } catch {
        if ("$_" -like 'SKIP:*') {
            Write-Host "$_"
            $exitCode = 2
        } else {
            Write-Host "FAIL: $Name - $_"
            $exitCode = 1
        }
    } finally {
        Stop-AllDcsb
        Restore-DcsbConfig -BackupPath $backup
    }
    exit $exitCode
}
