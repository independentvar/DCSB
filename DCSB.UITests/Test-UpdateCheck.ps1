# Verifies the full "update available" flow end to end - including a *real*
# download of the newest release's installer from GitHub.
#
# The locally built version is always >= the newest published release (every PR
# bumps the version before it releases), so a normal build can only ever reach
# the "No update available." branch. To exercise the interesting path - the
# GitHub call, version parsing/comparison, the "New version is available" offer,
# and the actual installer download - this test builds a throwaway copy of the
# app reporting version 0.0.0.1 into a temp folder and runs *that*. Against the
# real release feed that copy is out of date, so the app offers the newest
# release and, on "Yes", downloads its installer.
#
# The offer surfaces on its own from the startup auto-update check; the manual
# "Help -> Check for updates" is used as a fallback if it doesn't appear
# promptly (both hit the same code path).
#
# Side effects to be aware of: the DCSB installer requests admin, so the app
# launching it (which it does immediately after downloading) raises a UAC
# prompt. The download completes *before* that launch, so the test verifies the
# downloaded file and then kills the app - which cancels the pending elevated
# launch and dismisses the UAC prompt, so nothing is actually installed. Expect
# the screen to dim briefly during a run.
#
# Skips when the GitHub release feed is unreachable, when the newest release has
# no .exe installer asset, or when the .NET SDK isn't available to build the
# downgraded copy.
. "$PSScriptRoot\UITestHelpers.ps1"

Invoke-UITest -Name 'Update check offers the newest release and downloads its installer' -Body {
    $release = Get-DcsbNewestReleaseInstaller
    if (-not $release) { throw 'SKIP: could not reach the GitHub release feed to determine the newest release.' }
    if (-not $release.InstallerName) {
        throw "SKIP: newest release ($($release.TagName)) has no .exe installer asset to download."
    }
    Write-Host "  newest release: $($release.TagName) -> version $($release.Version), installer '$($release.InstallerName)' ($($release.InstallerSize) bytes)"

    $buildDir = Join-Path ([IO.Path]::GetTempPath()) ("dcsb-downgrade-" + [Guid]::NewGuid().ToString('N'))
    $installerPath = Join-Path ([IO.Path]::GetTempPath()) $release.InstallerName
    $downloadTmp = "$installerPath.download"
    $process = $null
    try {
        $exe = Build-DcsbDowngraded -Version '0.0.0.1' -OutputDir $buildDir

        $deviceNames = @(Get-OutputDeviceNames)
        $primary = if ($deviceNames.Count) { $deviceNames[0] } else { 'Disabled' }
        New-DcsbTestConfig -PrimaryOutput $primary

        # start clean so a fresh download is unmistakable, not a leftover file
        foreach ($stale in @($installerPath, $downloadTmp)) {
            if (Test-Path $stale) { Remove-Item $stale -Force -ErrorAction SilentlyContinue }
        }

        $process = Start-Dcsb -ExePath $exe
        $main = Get-DcsbMainWindow -ProcessId $process.Id
        Set-DcsbForeground $main
        Write-Host "  launched downgraded (v0.0.0.1) DCSB from $exe"

        # the startup auto-check should surface the offer on its own; fall back to
        # the manual Help -> Check for updates if it doesn't appear promptly
        $dialog = Wait-DcsbModalDialog $main -TimeoutSec 20
        if (-not $dialog) {
            Write-Host '  auto-check offer not seen; triggering Help -> Check for updates'
            Invoke-DcsbUpdateCheck $main
            $dialog = Wait-DcsbModalDialog $main -TimeoutSec 30
        }
        Assert-True ($null -ne $dialog) 'no update dialog appeared (neither the startup auto-check nor the manual check)'

        $title = $dialog.Current.Name
        $body = Get-DialogText $dialog
        Write-Host "  dialog: title='$title' body='$body'"
        if ($title -eq 'Update check failed') {
            Invoke-DialogButton $dialog 'No' | Out-Null  # 'No' avoids opening a browser
            throw "SKIP: update check could not reach the GitHub release feed ($body)."
        }

        # the offer must name the newest release version and the install action
        $expectedVersion = $release.Version.ToString()
        Assert-True ($title -like 'New version*') "expected a 'New version' offer dialog, got title='$title' body='$body'"
        Assert-True ($body -match [regex]::Escape($expectedVersion)) `
            "offer dialog does not mention newest version ${expectedVersion}: body='$body'"
        Assert-True ($body -match 'download and install') "unexpected offer dialog body='$body'"
        Write-Host "  offered update to version $expectedVersion"

        # accept -> the app downloads the real installer, then tries to launch it
        Assert-True (Invoke-DialogButton $dialog 'Yes') "the 'New version' offer dialog had no Yes button"
        Write-Host '  accepted; downloading the installer (this can take a while)...'
        Start-Sleep -Milliseconds 500  # let the offer dialog fully close before polling

        # Wait for the download to land. The app writes <installer>.download then
        # moves it to <installer>, so the final file appearing means the download
        # completed. Handle two dialogs that may interrupt: a duplicate offer from
        # a late auto-check (dismiss and keep waiting), or a download-failure box
        # (SKIP - the network dropped mid-download).
        $downloaded = $false
        $deadline = (Get-Date).AddSeconds(180)
        while ((Get-Date) -lt $deadline) {
            if (Test-Path $installerPath) { $downloaded = $true; break }
            $pending = Get-DcsbModalDialog $main
            if ($pending) {
                $pt = $pending.Current.Name
                $pb = Get-DialogText $pending
                Invoke-DialogButton $pending 'No' | Out-Null
                if ($pt -like 'New version*') {
                    Write-Host '  dismissed a duplicate offer from the startup auto-check'
                } elseif (Test-Path $installerPath) {
                    $downloaded = $true; break
                } else {
                    throw "SKIP: the download did not complete (${pt}): $pb"
                }
            }
            if ($process.HasExited -and (Test-Path $installerPath)) { $downloaded = $true; break }
            Start-Sleep -Milliseconds 200
        }

        # kill the app right away so any pending elevated installer launch (and its
        # UAC prompt) is cancelled before anything can be installed, and reap the
        # installer process if elevation somehow already went through
        if (-not $process.HasExited) { Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue }
        Stop-DcsbInstallerProcess -InstallerPath $installerPath

        Assert-True $downloaded "the installer '$($release.InstallerName)' was not downloaded to $installerPath within 180s"

        # the downloaded file must be the real installer: matching size and a PE header
        $info = Get-Item $installerPath
        Assert-True ($info.Length -gt 100kb) "downloaded installer is implausibly small ($($info.Length) bytes)"
        if ($release.InstallerSize -gt 0) {
            Assert-True ($info.Length -eq $release.InstallerSize) `
                "downloaded size $($info.Length) != release asset size $($release.InstallerSize)"
        }
        $stream = [IO.File]::OpenRead($installerPath)
        try { $b0 = $stream.ReadByte(); $b1 = $stream.ReadByte() } finally { $stream.Dispose() }
        Assert-True ($b0 -eq 0x4D -and $b1 -eq 0x5A) 'downloaded installer is not a PE executable (missing MZ header)'
        Write-Host "  downloaded and verified $($info.Length) bytes at $installerPath"
    }
    finally {
        if ($process -and -not $process.HasExited) { Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue }
        Stop-DcsbInstallerProcess -InstallerPath $installerPath
        foreach ($leftover in @($installerPath, $downloadTmp)) {
            if (Test-Path $leftover) { Remove-Item $leftover -Force -ErrorAction SilentlyContinue }
        }
        if (Test-Path $buildDir) { Remove-Item $buildDir -Recurse -Force -ErrorAction SilentlyContinue }
    }
}
