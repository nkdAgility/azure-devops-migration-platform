#Requires -Version 7
<#
.SYNOPSIS
    Validates platform/development/definition-of-workflow.json.

.DESCRIPTION
    1. Every shift's from-state is the initial state or a state some shift reaches.
    2. No shift's checks are all met by a brand-new Backlog Item: an issue whose body is exactly the
       issue template, with no comments or pull request. A check that matches template placeholder
       text would let unassessed work move on.

    Exit 0 valid, 1 invalid (each problem printed), 2 a file is missing or unreadable.
#>
[CmdletBinding()]
param(
    [string]$Definition = (Join-Path $PSScriptRoot '..\..\platform\development\definition-of-workflow.json'),
    [string]$Template = (Join-Path $PSScriptRoot '..\..\.github\ISSUE_TEMPLATE\backlog-item.md')
)

try {
    $dow = Get-Content -LiteralPath $Definition -Raw -ErrorAction Stop | ConvertFrom-Json -ErrorAction Stop
    $blank = Get-Content -LiteralPath $Template -Raw -ErrorAction Stop
}
catch {
    Write-Output "cannot read input: $($_.Exception.Message)"
    exit 2
}

# Drop the template's front matter; the issue body is what follows it.
$blank = $blank -replace '(?s)\A---.*?\r?\n---\r?\n', ''

$problems = [System.Collections.Generic.List[string]]::new()
$shifts = @($dow.shifts)
if ($shifts.Count -eq 0) { $problems.Add('the definition has no shifts') }

$reachable = @($dow.initialState) + @($shifts | ForEach-Object { $_.to })
foreach ($s in $shifts) {
    if ($s.from -notin $reachable) { $problems.Add("shift $($s.name): from-state '$($s.from)' is never reached") }
    $checks = @($s.checks | Where-Object { $_.type -eq 'pattern' })
    if ($checks.Count -eq 0) { continue }
    try {
        $met = -not ($checks | Where-Object { -not [regex]::IsMatch($blank, $_.pattern) })
    }
    catch {
        $problems.Add("shift $($s.name): a pattern does not compile: $($_.Exception.Message)")
        continue
    }
    if ($met) {
        $problems.Add("shift $($s.name): every check is met by a brand-new issue made from the template, so an item would move on without any work")
    }
}

if ($problems.Count) {
    $problems | ForEach-Object { Write-Output "FAIL $_" }
    exit 1
}
Write-Output "OK $($shifts.Count) shifts; none is met by a blank Backlog Item"
exit 0
