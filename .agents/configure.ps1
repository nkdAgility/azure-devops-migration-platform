#Requires -Version 5.1
<#
.SYNOPSIS
    Configures symlinks and hardlinks for Claude, GitHub Copilot, and Codex agent integration.

.DESCRIPTION
    Sets up the required file system links so that Claude (.claude/), GitHub (.github/),
    and Codex (AGENTS.md) all point to the canonical .agents/ directory.

    Run this script once after cloning, or whenever links need to be repaired.
    It registers .githooks as core.hooksPath, so post-checkout/post-merge hooks
    keep the hardlinks repaired automatically afterwards.

    Requires symlink creation privileges. On Windows, enable Developer Mode or run as Administrator.
    Hardlinks require no privileges: use -HardLinksOnly for unprivileged repair (used by git hooks).

.EXAMPLE
    .\.agents\configure.ps1

.EXAMPLE
    .\.agents\configure.ps1 -WhatIf

.EXAMPLE
    .\.agents\configure.ps1 -HardLinksOnly -Quiet
#>
[CmdletBinding(SupportsShouldProcess)]
param(
    # Repair hardlinks only; skip symlinks and the symlink-privilege check.
    [switch]$HardLinksOnly,
    # Report only changes and problems; suppress [OK] lines.
    [switch]$Quiet
)

$ErrorActionPreference = 'Stop'

$agentsDir = $PSScriptRoot
$repoRoot = Split-Path $agentsDir -Parent

# ---------------------------------------------------------------------------
# Helpers
# ---------------------------------------------------------------------------

function Write-Status {
    param(
        [ValidateSet('OK', 'CREATED', 'WARN', 'ERROR')]
        [string]$Status,
        [string]$Message
    )
    if ($Quiet -and $Status -eq 'OK') { return }
    $colors = @{ OK = 'Green'; CREATED = 'Cyan'; WARN = 'Yellow'; ERROR = 'Red' }
    Write-Host ("  [{0,-7}] {1}" -f $Status, $Message) -ForegroundColor $colors[$Status]
}

function Ensure-HardLink {
    <#
    .DESCRIPTION
        Creates a hardlink at $LinkPath pointing to the same inode as $TargetPath.
        Both paths are relative to the repo root.
    #>
    [CmdletBinding(SupportsShouldProcess)]
    param(
        [string]$LinkPath,
        [string]$TargetPath
    )

    $link = Join-Path $repoRoot $LinkPath
    $target = Join-Path $repoRoot $TargetPath

    if (-not (Test-Path $target)) {
        Write-Status ERROR "Target not found: $TargetPath"
        return
    }

    if (Test-Path $link) {
        $item = Get-Item -LiteralPath $link -Force
        if ($item.LinkType -eq 'HardLink') {
            $linkHash = (Get-FileHash $link   -Algorithm SHA256).Hash
            $targetHash = (Get-FileHash $target -Algorithm SHA256).Hash
            if ($linkHash -eq $targetHash) {
                Write-Status OK "$LinkPath  (hardlink, content matches)"
                return
            }
            Write-Status WARN "$LinkPath is a hardlink but content differs — recreating"
        }
        else {
            $kind = if ($item.LinkType) { $item.LinkType } else { 'file' }
            Write-Status WARN "$LinkPath exists as $kind — recreating as hardlink"
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
        $TargetRelative is the relative symlink value (e.g. "..\.agents\skills"), interpreted
        relative to the link's parent directory — exactly as the OS would resolve it.
    #>
    [CmdletBinding(SupportsShouldProcess)]
    param(
        [string]$LinkPath,
        [string]$TargetRelative
    )

    $link = Join-Path $repoRoot $LinkPath
    $linkParent = Split-Path $link -Parent
    $targetAbsolute = [System.IO.Path]::GetFullPath((Join-Path $linkParent $TargetRelative))

    if (-not (Test-Path $targetAbsolute)) {
        Write-Status ERROR "Target not found: $TargetRelative"
        return
    }

    if (Test-Path -LiteralPath $link) {
        $item = Get-Item -LiteralPath $link -Force
        if ($item.LinkType -eq 'SymbolicLink') {
            $existingAbsolute = [System.IO.Path]::GetFullPath((Join-Path $linkParent $item.Target))
            if ($existingAbsolute.TrimEnd('\') -eq $targetAbsolute.TrimEnd('\')) {
                Write-Status OK "$LinkPath  ->  $TargetRelative  (symlink already correct)"
                return
            }
            Write-Status WARN "$LinkPath symlink points elsewhere — recreating"
        }
        else {
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

# Check symlink privileges (required only for symlink operations)
if (-not $HardLinksOnly) {
    $isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
    $devMode = (Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\AppModelUnlock' -ErrorAction SilentlyContinue).AllowDevelopmentWithoutDevLicense -eq 1
    $canSymlink = $isAdmin -or $devMode

    if (-not $canSymlink -and -not $WhatIfPreference) {
        Write-Host ""
        Write-Host "  [ERROR  ] Cannot create symlinks — requires Administrator or Windows Developer Mode." -ForegroundColor Red
        Write-Host "            Enable Developer Mode:  Settings -> Privacy & Security -> For developers" -ForegroundColor Yellow
        Write-Host "            Or re-run as Administrator (or use -HardLinksOnly for link repair without symlinks)." -ForegroundColor Yellow
        Write-Host ""
        exit 1
    }
}

if (-not $Quiet) {
    Write-Host ""
    Write-Host "Configuring agent links" -ForegroundColor White
    Write-Host "Repo root : $repoRoot" -ForegroundColor DarkGray
    Write-Host ""
}

# -- Hardlinks (root-level entry-point files) --------------------------------
if (-not $Quiet) { Write-Host "Hardlinks:" -ForegroundColor White }
Ensure-HardLink 'AGENTS.md' '.agents\agents.md'
Ensure-HardLink 'CLAUDE.md' '.agents\agents.md'
Ensure-HardLink '.github\copilot-instructions.md' '.agents\agents.md'

# -- Nested agent stubs (directory-local AGENTS.md files) --------------------
# Each stub in .agents\40-stubs\ is hardlinked into the src/tests directories
# it governs. Add or remove entries here when adding or removing a stub.
if (-not $Quiet) {
    Write-Host ""
    Write-Host "Nested AGENTS.md stubs:" -ForegroundColor White
}
$stubs = @(
    @{ Source = '.agents\40-stubs\controlplane.md';          Targets = @(
        'src\DevOpsMigrationPlatform.ControlPlane\AGENTS.md',
        'src\DevOpsMigrationPlatform.ControlPlaneHost\AGENTS.md') }
    @{ Source = '.agents\40-stubs\cli.md';                   Targets = @(
        'src\DevOpsMigrationPlatform.CLI.Migration\AGENTS.md') }
    @{ Source = '.agents\40-stubs\infrastructure-agent.md';  Targets = @(
        'src\DevOpsMigrationPlatform.Infrastructure.Agent\AGENTS.md') }
    @{ Source = '.agents\40-stubs\migration-agent.md';       Targets = @(
        'src\DevOpsMigrationPlatform.MigrationAgent\AGENTS.md') }
    @{ Source = '.agents\40-stubs\net481.md';                Targets = @(
        'src\DevOpsMigrationPlatform.TfsMigrationAgent\AGENTS.md',
        'src\DevOpsMigrationPlatform.Infrastructure.TfsObjectModel\AGENTS.md') }
    @{ Source = '.agents\40-stubs\simulated-connector.md';   Targets = @(
        'src\DevOpsMigrationPlatform.Infrastructure.Simulated\AGENTS.md') }
    @{ Source = '.agents\40-stubs\azuredevops-connector.md'; Targets = @(
        'src\DevOpsMigrationPlatform.Infrastructure.AzureDevOps\AGENTS.md') }
    @{ Source = '.agents\40-stubs\abstractions.md';          Targets = @(
        'src\DevOpsMigrationPlatform.Abstractions\AGENTS.md',
        'src\DevOpsMigrationPlatform.Abstractions.Agent\AGENTS.md',
        'src\DevOpsMigrationPlatform.Abstractions.ControlPlane\AGENTS.md',
        'src\DevOpsMigrationPlatform.Abstractions.Storage\AGENTS.md') }
    @{ Source = '.agents\40-stubs\tests.md';                 Targets = @(
        'tests\AGENTS.md') }
)
foreach ($stub in $stubs) {
    foreach ($target in $stub.Targets) {
        Ensure-HardLink $target $stub.Source
    }
}

# -- .claude symlinks --------------------------------------------------------
Write-Host ""
Write-Host ".claude/ symlinks:" -ForegroundColor White
Ensure-Symlink '.claude\skills'   '..\.agents\skills'
Ensure-Symlink '.claude\agents'   '..\.agents\agents'
Ensure-Symlink '.claude\commands' '..\.agents\commands'
Ensure-Symlink '.claude\prompts'  '..\.agents\prompts'
Ensure-Symlink '.claude\workflows'  '..\.agents\workflows'

# -- .github symlinks --------------------------------------------------------
Write-Host ""
Write-Host ".github/ symlinks:" -ForegroundColor White
Ensure-Symlink '.github\agents'  '..\.agents\agents'
Ensure-Symlink '.github\prompts' '..\.agents\prompts'

Write-Host ""
Write-Host "Done." -ForegroundColor Green
Write-Host ""
