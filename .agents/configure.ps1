#Requires -Version 5.1
<#
.SYNOPSIS
    Configures symlinks and hardlinks for Claude, GitHub Copilot, and Codex agent integration.

.DESCRIPTION
    Sets up the required file system links so that Claude (.claude/), GitHub (.github/),
    and Codex (AGENTS.md) all point to the canonical .agents/ directory.

    Run this script once after cloning, or whenever links need to be repaired.

    Requires symlink creation privileges. On Windows, enable Developer Mode or run as Administrator.

.EXAMPLE
    .\.agents\configure.ps1

.EXAMPLE
    .\.agents\configure.ps1 -WhatIf
#>
[CmdletBinding(SupportsShouldProcess)]
param()

$ErrorActionPreference = 'Stop'

$agentsDir = $PSScriptRoot
$repoRoot  = Split-Path $agentsDir -Parent

# ---------------------------------------------------------------------------
# Helpers
# ---------------------------------------------------------------------------

function Write-Status {
    param(
        [ValidateSet('OK','CREATED','SKIPPED','WARN','ERROR')]
        [string]$Status,
        [string]$Message
    )
    $colors = @{ OK='Green'; CREATED='Cyan'; SKIPPED='DarkGray'; WARN='Yellow'; ERROR='Red' }
    Write-Host ("  [{0,-7}] {1}" -f $Status, $Message) -ForegroundColor $colors[$Status]
}

function Get-FileNodeId {
    param([string]$Path)
    $fs = [System.IO.FileStream]::new($Path, [System.IO.FileMode]::Open, [System.IO.FileAccess]::Read, [System.IO.FileShare]::ReadWrite)
    $handle = $fs.SafeFileHandle
    $info = [System.Runtime.InteropServices.Marshal]::AllocHGlobal(52)
    try {
        # Use GetFileInformationByHandle via P/Invoke would be ideal, but
        # comparing hashes is sufficient for our purposes.
        return (Get-FileHash -Path $Path -Algorithm SHA256).Hash
    } finally {
        $fs.Dispose()
    }
}

function Ensure-HardLink {
    <#
    .DESCRIPTION
        Creates a hardlink at $LinkPath pointing to the same inode as $TargetPath.
        Both paths are relative to the repo root.
    #>
    param(
        [string]$LinkPath,
        [string]$TargetPath
    )

    $link   = Join-Path $repoRoot $LinkPath
    $target = Join-Path $repoRoot $TargetPath

    if (-not (Test-Path $target)) {
        Write-Status ERROR "Target not found: $TargetPath"
        return
    }

    if (Test-Path $link) {
        $item = Get-Item -LiteralPath $link -Force
        if ($item.LinkType -eq 'HardLink') {
            $linkHash   = (Get-FileHash $link -Algorithm SHA256).Hash
            $targetHash = (Get-FileHash $target -Algorithm SHA256).Hash
            if ($linkHash -eq $targetHash) {
                Write-Status OK      "$LinkPath  (hardlink, content matches)"
                return
            }
            Write-Status WARN "$LinkPath is a hardlink but content differs — recreating"
        } else {
            Write-Status WARN "$LinkPath exists as $($item.LinkType ?? 'file') — recreating as hardlink"
        }
        if ($PSCmdlet.ShouldProcess($link, 'Remove')) { Remove-Item -LiteralPath $link -Force }
    }

    if ($PSCmdlet.ShouldProcess($link, "Create hardlink -> $TargetPath")) {
        New-Item -ItemType HardLink -Path $link -Target $target | Out-Null
        Write-Status CREATED "$LinkPath  ->  $TargetPath  (hardlink)"
    }
}

function Ensure-Symlink {
    <#
    .DESCRIPTION
        Creates a directory symlink at $LinkPath pointing to $TargetRelative.
        $LinkPath is relative to repo root.
        $TargetRelative is the relative path used as the symlink value (e.g. "..\.agents\skills").
    #>
    param(
        [string]$LinkPath,
        [string]$TargetRelative
    )

    $link        = Join-Path $repoRoot $LinkPath
    $linkParent  = Split-Path $link -Parent
    # Resolve the symlink target relative to the link's parent directory (as the OS would)
    $targetAbsolute = Join-Path $linkParent $TargetRelative
    $targetAbsolute = [System.IO.Path]::GetFullPath($targetAbsolute)

    if (-not (Test-Path $targetAbsolute)) {
        Write-Status ERROR "Target not found: $TargetRelative"
        return
    }

    if (Test-Path -LiteralPath $link) {
        $item = Get-Item -LiteralPath $link -Force
        if ($item.LinkType -eq 'SymbolicLink') {
            # Resolve both to absolute for comparison
            $resolvedTarget = (Resolve-Path -LiteralPath $targetAbsolute).Path.TrimEnd('\')
            $resolvedLink   = (Resolve-Path -LiteralPath $item.Target -ErrorAction SilentlyContinue)
            if ($resolvedLink -and $resolvedLink.Path.TrimEnd('\') -eq $resolvedTarget) {
                Write-Status OK      "$LinkPath  ->  $TargetRelative  (symlink already correct)"
                return
            }
            Write-Status WARN "$LinkPath symlink points elsewhere — recreating"
        } else {
            Write-Status WARN "$LinkPath exists as directory/file — replacing with symlink"
        }
        if ($PSCmdlet.ShouldProcess($link, 'Remove')) { Remove-Item -LiteralPath $link -Recurse -Force }
    }

    if ($PSCmdlet.ShouldProcess($link, "Create symlink -> $TargetRelative")) {
        New-Item -ItemType SymbolicLink -Path $link -Target $TargetRelative | Out-Null
        Write-Status CREATED "$LinkPath  ->  $TargetRelative  (symlink)"
    }
}

# ---------------------------------------------------------------------------
# Main
# ---------------------------------------------------------------------------

Write-Host ""
Write-Host "Configuring agent links" -ForegroundColor White
Write-Host "Repo root : $repoRoot" -ForegroundColor DarkGray
Write-Host ""

# -- Hardlinks (root-level entry-point files) --------------------------------
Write-Host "Hardlinks:" -ForegroundColor White
Ensure-HardLink 'AGENTS.md' '.agents\agents.md'
Ensure-HardLink 'CLAUDE.md' '.agents\agents.md'

# -- .claude symlinks --------------------------------------------------------
Write-Host ""
Write-Host ".claude/ symlinks:" -ForegroundColor White
Ensure-Symlink '.claude\skills'   '..\.agents\skills'
Ensure-Symlink '.claude\agents'   '..\.agents\agents'
Ensure-Symlink '.claude\commands' '..\.agents\commands'

# -- .github symlinks --------------------------------------------------------
Write-Host ""
Write-Host ".github/ symlinks:" -ForegroundColor White
Ensure-Symlink '.github\agents'  '..\.agents\agents'
Ensure-Symlink '.github\prompts' '..\.agents\prompts'

Write-Host ""
Write-Host "Done." -ForegroundColor Green
Write-Host ""
