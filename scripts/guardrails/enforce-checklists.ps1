param(
    [string]$FeatureDir
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($FeatureDir)) {
    throw "FeatureDir is required."
}

$checklistsDir = Join-Path $FeatureDir "checklists"
if (-not (Test-Path -LiteralPath $checklistsDir)) {
    Write-Host "No checklists directory found at '$checklistsDir'. Gate passed."
    exit 0
}

$files = Get-ChildItem -LiteralPath $checklistsDir -File -Filter "*.md"
if (-not $files -or $files.Count -eq 0) {
    Write-Host "No checklist markdown files found in '$checklistsDir'. Gate passed."
    exit 0
}

$results = @()
foreach ($file in $files) {
    $lines = Get-Content -LiteralPath $file.FullName
    $total = ($lines | Where-Object { $_ -match '^\s*-\s\[( |x|X)\]\s' }).Count
    $completed = ($lines | Where-Object { $_ -match '^\s*-\s\[(x|X)\]\s' }).Count
    $incomplete = ($lines | Where-Object { $_ -match '^\s*-\s\[ \]\s' }).Count
    $status = if ($incomplete -eq 0) { "PASS" } else { "FAIL" }

    $results += [PSCustomObject]@{
        Checklist  = $file.Name
        Total      = $total
        Completed  = $completed
        Incomplete = $incomplete
        Status     = $status
    }
}

$results | Sort-Object Checklist | Format-Table -AutoSize | Out-Host

$failed = $results | Where-Object { $_.Incomplete -gt 0 }
if ($failed) {
    throw "Checklist enforcement failed. Incomplete checklist items remain."
}

Write-Host "Checklist enforcement passed."
