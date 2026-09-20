# Runs the same checks as CI on a local checkout: the managed builds with
# --warnaserror, all three test projects, and both parity scripts. It stops at the
# first failure and prints a compact summary.
#
# CI runs these steps itself; this script only mirrors them for local use, so it
# deliberately does not force the native-bridge properties the Windows workflow
# sets (a local build keeps its own IncludeAsioBridge / IncludeCwAsioBridge /
# IncludeAirPlay2Bridge defaults).
#
# Usage:
#   pwsh -NoProfile -File scripts/verify-all.ps1
#   pwsh -NoProfile -File scripts/verify-all.ps1 -Configuration Release
#   pwsh -NoProfile -File scripts/verify-all.ps1 -SkipBuild

[CmdletBinding()]
param(
    # Build and test configuration.
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug',

    # Skip the managed builds and only run the tests and parity checks.
    [switch]$SkipBuild,

    # Skip the test projects and only run the builds and parity checks.
    [switch]$SkipTests
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$results = [System.Collections.Generic.List[object]]::new()

function Invoke-Check {
    param(
        [string]$Name,
        [string]$Executable,
        [string[]]$Arguments
    )

    Write-Host "==> $Name" -ForegroundColor Cyan
    $timer = [System.Diagnostics.Stopwatch]::StartNew()
    try {
        $output = & $Executable @Arguments 2>&1
        $exitCode = $LASTEXITCODE
    }
    catch {
        $output = @($_.Exception.Message)
        $exitCode = 1
    }
    $timer.Stop()
    $seconds = [math]::Round($timer.Elapsed.TotalSeconds, 1)

    if ($exitCode -eq 0) {
        Write-Host "    OK ($seconds s)" -ForegroundColor Green
    }
    else {
        Write-Host "    FAILED ($seconds s, exit $exitCode)" -ForegroundColor Red
        # Show only the tail so the summary stays readable.
        $output | Select-Object -Last 40 | ForEach-Object { Write-Host "    $_" }
    }

    $results.Add([pscustomobject]@{
        Name    = $Name
        Ok      = ($exitCode -eq 0)
        Seconds = $seconds
    })
    return ($exitCode -eq 0)
}

$steps = [System.Collections.Generic.List[object]]::new()
if (-not $SkipBuild) {
    foreach ($project in 'Orynivo.Core', 'Orynivo.Server', 'Orynivo') {
        $steps.Add(@{
            Name       = "Build $project ($Configuration)"
            Executable = 'dotnet'
            Arguments  = @(
                'build', "$project/$project.csproj",
                '--configuration', $Configuration,
                '--warnaserror',
                '--verbosity', 'minimal'
            )
        })
    }

    # The desktop swaps in Compatibility/Linux implementations for several Windows types,
    # so a Windows-only local build cannot see a broken Linux or macOS call site. Overriding
    # the OS property compiles that variant on any host; this caught a CreateAsync overload
    # mismatch that only the CI Linux and macOS jobs had reported.
    $steps.Add(@{
        Name       = "Build Orynivo (Linux/macOS compile, $Configuration)"
        Executable = 'dotnet'
        Arguments  = @(
            'build', 'Orynivo/Orynivo.csproj',
            '-p:OS=Unix',
            '--configuration', $Configuration,
            '--warnaserror',
            '--verbosity', 'minimal'
        )
    })
}
if (-not $SkipTests) {
    foreach ($project in 'Orynivo.Core.Tests', 'Orynivo.Tests', 'Orynivo.Server.Tests') {
        $steps.Add(@{
            Name       = "Test $project"
            Executable = 'dotnet'
            Arguments  = @('test', "$project/$project.csproj", '--configuration', $Configuration)
        })
    }
}
$steps.Add(@{
    Name       = 'Verify MCP tool parity'
    Executable = 'pwsh'
    Arguments  = @('-NoProfile', '-File', 'scripts/verify-mcp-tool-parity.ps1')
})
$steps.Add(@{
    Name       = 'Verify localization parity'
    Executable = 'pwsh'
    Arguments  = @('-NoProfile', '-File', 'scripts/verify-localization-parity.ps1')
})
$steps.Add(@{
    Name       = 'Verify GitHub Actions pins'
    Executable = 'pwsh'
    Arguments  = @('-NoProfile', '-File', 'scripts/verify-github-actions-pins.ps1')
})

Push-Location $root
try {
    $failed = $false
    foreach ($step in $steps) {
        if (-not (Invoke-Check -Name $step.Name -Executable $step.Executable -Arguments $step.Arguments)) {
            $failed = $true
            break
        }
    }

    Write-Host ''
    Write-Host 'Summary'
    foreach ($result in $results) {
        $status = if ($result.Ok) { 'OK  ' } else { 'FAIL' }
        $color = if ($result.Ok) { 'Green' } else { 'Red' }
        Write-Host ("  {0}  {1,7} s  {2}" -f $status, $result.Seconds, $result.Name) -ForegroundColor $color
    }
    $skipped = $steps.Count - $results.Count
    if ($skipped -gt 0) {
        Write-Host "  $skipped step(s) not run after the failure." -ForegroundColor Yellow
    }

    if ($failed) {
        Write-Host ''
        Write-Host 'Verification failed.' -ForegroundColor Red
        exit 1
    }

    Write-Host ''
    Write-Host 'All checks passed.' -ForegroundColor Green
    exit 0
}
finally {
    Pop-Location
}
