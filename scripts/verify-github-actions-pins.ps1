# Verifies that every GitHub Actions reference uses one consistent version per
# action across all workflows.
#
# Why this exists: Dependabot's github_actions updater fails with
# "Error processing <action> (RuntimeError) / No files changed!" when the same
# action is referenced with different major versions in different workflow files.
# Mixed majors still run fine in CI, so nothing else catches the drift.
#
# Usage:
#   pwsh -NoProfile -File scripts/verify-github-actions-pins.ps1

[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$workflows = Join-Path $root '.github/workflows'

if (-not (Test-Path -LiteralPath $workflows)) {
    Write-Host 'No .github/workflows directory found; nothing to verify.'
    exit 0
}

$references = [System.Collections.Generic.List[object]]::new()
foreach ($file in Get-ChildItem -LiteralPath $workflows -Filter '*.yml' -File) {
    $lineNumber = 0
    foreach ($line in [System.IO.File]::ReadAllLines($file.FullName)) {
        $lineNumber++
        # Workflow steps are YAML list items, so "uses:" may follow a "- " marker.
        if ($line -notmatch '^\s*-?\s*uses:\s*["'']?(?<reference>[^\s#"'' ]+)') {
            continue
        }

        $reference = $Matches['reference'].Trim()
        # Local composite actions and container references are not versioned here.
        if ($reference.StartsWith('./') -or $reference.StartsWith('docker://') -or $reference -notmatch '@') {
            continue
        }

        $parts = $reference.Split('@', 2)
        $references.Add([pscustomobject]@{
            Action = $parts[0]
            Ref    = $parts[1]
            File   = $file.Name
            Line   = $lineNumber
        })
    }
}

$failures = [System.Collections.Generic.List[string]]::new()
foreach ($group in $references | Group-Object -Property Action) {
    $refs = @($group.Group.Ref | Sort-Object -Unique)
    if ($refs.Count -le 1) {
        continue
    }

    $locations = ($group.Group | ForEach-Object { "$($_.File):$($_.Line)@$($_.Ref)" }) -join ', '
    $failures.Add("$($group.Name) uses $($refs -join ', ') - $locations")
}

if ($failures.Count -gt 0) {
    Write-Host 'Inconsistent GitHub Actions versions:' -ForegroundColor Red
    foreach ($failure in $failures) {
        Write-Host "  $failure" -ForegroundColor Red
    }
    Write-Host ''
    Write-Host 'Dependabot fails with "No files changed!" for actions that are pinned to' -ForegroundColor Yellow
    Write-Host 'different versions in different workflow files. Use one version per action.' -ForegroundColor Yellow
    exit 1
}

$distinct = ($references | Select-Object -ExpandProperty Action -Unique).Count
Write-Host "GitHub Actions pins are consistent ($($references.Count) references, $distinct actions)."
exit 0
