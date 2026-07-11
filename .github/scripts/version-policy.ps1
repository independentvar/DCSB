[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$BaseRef,

    [string]$HeadRef = 'HEAD',

    [ValidateSet('Direct', 'MergeBase')]
    [string]$DiffMode = 'Direct'
)

$ErrorActionPreference = 'Stop'

function Get-ChangedFiles {
    if ($BaseRef -match '^0+$') {
        return @('__initial_push__')
    }

    $range = if ($DiffMode -eq 'MergeBase') {
        "$BaseRef...$HeadRef"
    } else {
        "$BaseRef..$HeadRef"
    }

    $files = @(git diff --name-only $range)
    if ($LASTEXITCODE -ne 0) {
        throw "git diff failed for $range"
    }

    return $files | Where-Object { $_ }
}

function Test-IsShippedCode([string]$Path) {
    $normalized = $Path.Replace('\', '/')

    # Keep this policy in one place: both PR validation and release detection
    # use it. Tests, automation, installer tooling/output, and documentation do
    # not alter the application binaries that carry AssemblyVersion.
    $nonShippedPattern = '^(DCSB\.Tests/|DCSB\.UITests/|\.github/|Installer scripts/|Installers/)|\.md$|\.png$|^LICENSE$|^\.gitignore$'
    return $normalized -notmatch $nonShippedPattern
}

function Get-AssemblyVersion([string]$Ref) {
    $content = git show "${Ref}:AssemblyVersionInfo.cs"
    if ($LASTEXITCODE -ne 0) {
        throw "Could not read AssemblyVersionInfo.cs at $Ref"
    }
    $text = $content -join "`n"

    $assemblyMatch = [regex]::Match(
        $text,
        '(?m)^\s*\[assembly:\s*AssemblyVersion\("(?<version>\d+\.\d+\.\d+\.\d+)"\)\]\s*$'
    )
    $fileMatch = [regex]::Match(
        $text,
        '(?m)^\s*\[assembly:\s*AssemblyFileVersion\("(?<version>\d+\.\d+\.\d+\.\d+)"\)\]\s*$'
    )

    if (-not $assemblyMatch.Success -or -not $fileMatch.Success) {
        throw "Could not parse both four-part assembly versions at $Ref"
    }

    $assemblyVersion = $assemblyMatch.Groups['version'].Value
    $fileVersion = $fileMatch.Groups['version'].Value
    if ($assemblyVersion -ne $fileVersion) {
        throw "AssemblyVersion ($assemblyVersion) and AssemblyFileVersion ($fileVersion) differ at $Ref"
    }

    return [version]$assemblyVersion
}

$changedFiles = @(Get-ChangedFiles)
$shippedFiles = @($changedFiles | Where-Object { Test-IsShippedCode $_ })
$requiresBump = $shippedFiles.Count -gt 0

Write-Host 'Changed files:'
$changedFiles | ForEach-Object { Write-Host "  $_" }
Write-Host 'Files affecting shipped binaries:'
$shippedFiles | ForEach-Object { Write-Host "  $_" }

# When shipped code changed, require AssemblyVersion to have been increased.
# This throws on a missing bump; the final boolean lets a caller reuse this one
# invocation both to validate the bump and to decide whether a release is due.
if (-not $requiresBump) {
    Write-Host 'No AssemblyVersion bump is required.'
} elseif ($BaseRef -match '^0+$') {
    Write-Host 'Initial push: no earlier version is available for comparison.'
} else {
    $baseVersion = Get-AssemblyVersion $BaseRef
    $headVersion = Get-AssemblyVersion $HeadRef
    Write-Host "AssemblyVersion: $baseVersion -> $headVersion"

    if ($headVersion -le $baseVersion) {
        throw "Shipped code changed, but AssemblyVersionInfo.cs was not increased (base: $baseVersion, head: $headVersion)."
    }

    Write-Host 'AssemblyVersion and AssemblyFileVersion are equal and were increased.'
}

Write-Output $requiresBump.ToString().ToLowerInvariant()
