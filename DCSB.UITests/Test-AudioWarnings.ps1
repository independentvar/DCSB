# Verifies that Settings -> Sound explains invalid audio routing inline and
# removes derived warnings immediately when the user fixes the selection.
. "$PSScriptRoot\UITestHelpers.ps1"

Invoke-UITest -Name 'Sound settings show actionable inline audio warnings' -Body {
    $deviceNames = Get-OutputDeviceNames
    Assert-True ($deviceNames.Count -gt 0) 'at least one active output device is required'
    $device = $deviceNames[0]

    # A missing persisted endpoint is disabled during startup, but its original
    # name and the reason must survive long enough to be useful in Settings.
    $inputName = $null
    $enumerator = New-Object NAudio.CoreAudioApi.MMDeviceEnumerator
    try {
        $inputs = $enumerator.EnumerateAudioEndPoints([NAudio.CoreAudioApi.DataFlow]::Capture, [NAudio.CoreAudioApi.DeviceState]::Active)
        foreach ($input in $inputs) {
            if ($input.FriendlyName -notmatch '(?i)cable|voicemeeter|virtual audio|blackhole|loopback') {
                $inputName = $input.FriendlyName
                break
            }
        }
    } finally {
        $enumerator.Dispose()
    }

    $config = New-DcsbConfig -PrimaryOutput 'UITest Missing Output'
    if ($inputName) { $config.MicrophoneInput = $inputName }
    Save-DcsbConfigModel $config
    $process = Start-Dcsb
    $main = Get-DcsbMainWindow -ProcessId $process.Id
    $settings = Open-DcsbSettings $main
    Select-DcsbSettingsTab $settings 'Sound'

    $missingWarning = Find-DescendantByName $settings "First output 'UITest Missing Output' is unavailable and was disabled."
    Assert-True ($null -ne $missingWarning) 'missing output failure was not shown inline'

    if ($inputName) {
        $nowhereText = 'Microphone passthrough is enabled, but the second output is disabled; your voice has nowhere to go.'
        Assert-True ($null -ne (Find-DescendantByName $settings $nowhereText)) 'microphone-to-disabled-output warning was not shown'
    }

    $combos = Get-OutputDeviceCombos $settings
    Select-ComboItem $combos[0] $device
    Start-Sleep -Milliseconds 300
    $staleWarning = Find-DescendantByName $settings "First output 'UITest Missing Output' is unavailable and was disabled."
    Assert-True ($null -eq $staleWarning) 'runtime failure remained after selecting a working output'

    Select-ComboItem $combos[1] $device
    Start-Sleep -Milliseconds 300
    $duplicateText = 'The first and second outputs are the same device; every sound will be played twice.'
    Assert-True ($null -ne (Find-DescendantByName $settings $duplicateText)) 'duplicate-output warning was not shown'

    if ($inputName -and $device -notmatch '(?i)cable|voicemeeter|virtual audio|blackhole|loopback') {
        $nonCableText = "The second output ('$device') does not look like a virtual cable; microphone passthrough may not reach voice chat."
        Assert-True ($null -ne (Find-DescendantByName $settings $nonCableText)) 'non-virtual secondary warning was not shown'
    }

    Select-ComboItem $combos[1] 'Disabled'
    Start-Sleep -Milliseconds 300
    Assert-True ($null -eq (Find-DescendantByName $settings $duplicateText)) 'duplicate-output warning did not disappear after fixing the route'

    Close-DcsbSettings $settings
    Stop-Dcsb $process
}
