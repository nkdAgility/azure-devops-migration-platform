# SPDX-License-Identifier: AGPL-3.0-only
# Copyright (c) Naked Agility Limited

param(
    [Parameter(Mandatory = $false)]
    [string]$LogPath = ".output",

    [Parameter(Mandatory = $false)]
    [string]$SearchPattern = "*.log"
)

if (-not (Test-Path $LogPath)) {
    Write-Error "Log path '$LogPath' was not found."
    exit 1
}

$files = Get-ChildItem -Path $LogPath -Recurse -File -Include $SearchPattern
$lifecycleLines = foreach ($file in $files) {
    Select-String -Path $file.FullName -Pattern "ProjectLifecycle outcome" -SimpleMatch
}

$total = @($lifecycleLines).Count
$failedTeardown = @($lifecycleLines | Where-Object { $_.Line -match "teardown=Failed" }).Count
$manualIntervention = @($lifecycleLines | Where-Object { $_.Line -match "teardown=Failed" -or $_.Line -match "teardownReason=" }).Count

Write-Output "Lifecycle runs observed: $total"
Write-Output "Failed teardowns: $failedTeardown"
Write-Output "Runs requiring manual cleanup review: $manualIntervention"

if ($total -gt 0) {
    $rate = [math]::Round(($manualIntervention / $total) * 100, 2)
    Write-Output "Manual cleanup review rate: $rate%"
}
