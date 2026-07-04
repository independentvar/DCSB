# Runs all DCSB UI/integration tests (Test-*.ps1 in this folder), each in its
# own PowerShell process so a crashed test cannot take the runner down.
# Any DCSB instance running before the tests is closed and restarted afterwards.
#
#   .\Run-UITests.ps1                # run everything
#   .\Run-UITests.ps1 -Filter Playback   # run tests whose file name matches
#
# Exit code = number of failed tests.
param(
    [string]$Filter = ''
)

$ErrorActionPreference = 'Stop'
. "$PSScriptRoot\UITestHelpers.ps1"

$tests = Get-ChildItem $PSScriptRoot -Filter 'Test-*.ps1' | Sort-Object Name
if ($Filter) { $tests = $tests | Where-Object { $_.BaseName -match $Filter } }
if (-not $tests) { Write-Host "No tests match filter '$Filter'."; exit 1 }

# remember a running DCSB (e.g. the installed release) so it can be restarted
$previousInstances = @(Get-Process DCSB -ErrorAction SilentlyContinue | ForEach-Object { $_.Path } | Sort-Object -Unique)
if ($previousInstances.Count) { Write-Host "Running DCSB found, will restart after tests: $($previousInstances -join ', ')" }

# each test backs up and restores the config itself; this outer backup is a
# safety net in case a test process dies before its own restore runs
Initialize-UITestContext
Stop-AllDcsb
$outerBackup = Backup-DcsbConfig

$shell = (Get-Process -Id $PID).Path  # reuse whichever PowerShell launched us
$passed = @(); $failed = @(); $skipped = @()

try {
    foreach ($test in $tests) {
        Write-Host ''
        Write-Host "=== $($test.BaseName) ===" -ForegroundColor Cyan
        & $shell -NoProfile -ExecutionPolicy Bypass -File $test.FullName
        switch ($LASTEXITCODE) {
            0 { $passed += $test.BaseName }
            2 { $skipped += $test.BaseName }
            default { $failed += $test.BaseName }
        }
    }
} finally {
    Stop-AllDcsb
    Restore-DcsbConfig -BackupPath $outerBackup
    foreach ($path in $previousInstances) {
        if ($path -and (Test-Path $path)) {
            Write-Host "Restarting $path"
            Start-Process $path
        }
    }
}

Write-Host ''
Write-Host ("Passed: {0}  Failed: {1}  Skipped: {2}" -f $passed.Count, $failed.Count, $skipped.Count) -ForegroundColor $(if ($failed.Count) { 'Red' } else { 'Green' })
foreach ($name in $failed) { Write-Host "  FAILED: $name" -ForegroundColor Red }
foreach ($name in $skipped) { Write-Host "  SKIPPED: $name" -ForegroundColor Yellow }
exit $failed.Count
