#Requires -Version 7
<#
.SYNOPSIS
    Fails when the repository uses Python anywhere.

.DESCRIPTION
    Martin, 2026-11-13: "At no point on any level should python ever be used."
    Fails on any tracked Python file (.py, .pyw, .pyi, .ipynb), and on any tracked workflow,
    script, hook or build file that runs Python or a Python tool, or sets up Python.
    Exit 0 clean, 1 Python found (each finding printed), 2 not a git repository.
#>
[CmdletBinding()]
param([string]$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path)

$files = @(git -C $RepoRoot ls-files 2>$null)
if ($LASTEXITCODE -ne 0) { Write-Output "not a git repository: $RepoRoot"; exit 2 }

$findings = [System.Collections.Generic.List[string]]::new()
foreach ($f in $files) {
    if ($f -match '\.(py|pyw|pyi|ipynb)$') { $findings.Add("$f is a Python file") }
}

# Files that run things: workflows, scripts, hooks, build files.
$runnable = $files | Where-Object {
    $_ -match '^\.github/workflows/' -or $_ -match '\.(ps1|psm1|sh|cmd|bat|yml|yaml)$' -or $_ -match '^\.githooks/' -or $_ -eq 'build.ps1'
} | Where-Object { $_ -notmatch '(^|/)tests?/.*\.Tests\.ps1$' -and $_ -notmatch 'Test-NoPython\.ps1$' -and $_ -notmatch 'Deny-Python\.ps1$' }

$uses = '(?im)(^|[;&|(]|\s&|\$\(|run:\s*|^\s*-?\s*)\s*(python[0-9.]*|py|pip[0-9.]*|pipx|uv|uvx|poetry|conda|pyenv)(\.exe)?(\s|$)|setup-python|Get-Command\s+python|python-version'
foreach ($f in $runnable) {
    $path = Join-Path $RepoRoot $f
    if (-not (Test-Path -LiteralPath $path)) { continue }
    $n = 0
    foreach ($line in [IO.File]::ReadLines($path)) {
        $n++
        if ($line -match $uses) { $findings.Add("${f}:${n}: $($line.Trim())") }
    }
}

if ($findings.Count) {
    $findings | ForEach-Object { Write-Output "PYTHON $_" }
    exit 1
}
Write-Output "OK no Python in $($files.Count) tracked files"
exit 0
