param(
    [string]$FeatureDir
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($FeatureDir)) {
    throw "FeatureDir is required."
}

$specPath = Join-Path $FeatureDir "spec.md"
$tasksPath = Join-Path $FeatureDir "tasks.md"

if (-not (Test-Path -LiteralPath $specPath)) {
    throw "Missing spec file: $specPath"
}

if (-not (Test-Path -LiteralPath $tasksPath)) {
    throw "Missing tasks file: $tasksPath"
}

$specLines = Get-Content -LiteralPath $specPath
$taskLines = Get-Content -LiteralPath $tasksPath

# Requirement IDs from common spec patterns: R01, R-01, FR-001
$requirementIds = @(
    $specLines | ForEach-Object {
        if ($_ -match '\b(R-?\d{2,}|FR-\d{3,})\b') { $Matches[1] }
    }
) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Sort-Object -Unique

if (-not $requirementIds -or $requirementIds.Count -eq 0) {
    throw "No requirement IDs found in spec.md. Add stable requirement IDs (for example R01/FR-001)."
}

$unmapped = @()
foreach ($id in $requirementIds) {
    $escaped = [Regex]::Escape($id)
    $mapped = $taskLines | Where-Object { $_ -match "\b$escaped\b" }
    if (-not $mapped) {
        $unmapped += $id
    }
}

if ($unmapped.Count -gt 0) {
    throw ("Task coverage enforcement failed. Unmapped requirements: {0}" -f ($unmapped -join ", "))
}

Write-Host ("Task coverage enforcement passed. Mapped requirements: {0}" -f ($requirementIds.Count))
