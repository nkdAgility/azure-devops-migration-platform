#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Single-entry-point build script for the Azure DevOps Migration Platform.

.DESCRIPTION
    Resolves version via GitVersion, builds all components, runs tests,
    publishes binaries, and packages distributable artefacts.

    Prerequisites:
      - .NET SDK (see global.json)
      - GitVersion.Tool 6.1.0 installed as a global dotnet tool:
            dotnet tool install --global GitVersion.Tool --version 6.1.0
        OR restore from the local tool manifest:
            dotnet tool restore

    Artefact outputs (placed under ./artifacts/):
      - MigrationTools-{SemVer}.zip   — CLI tools (devopsMigration + TfsMigration)
      - ControlPlane-{SemVer}.zip     — Control Plane host
      - Agent-{SemVer}.zip            — Migration Agent worker
#>

$ErrorActionPreference = 'Stop'
$ProgressPreference    = 'SilentlyContinue'

$RepoRoot       = $PSScriptRoot
$SolutionFile   = Join-Path $RepoRoot 'DevOpsMigrationPlatform.slnx'
$ArtifactsDir   = Join-Path $RepoRoot 'artifacts'
$TestResultsDir = Join-Path $RepoRoot 'TestResults'

$CliMigrationProject = Join-Path $RepoRoot 'src/DevOpsMigrationPlatform.CLI.Migration/DevOpsMigrationPlatform.CLI.Migration.csproj'
$CliTfsProject       = Join-Path $RepoRoot 'src/DevOpsMigrationPlatform.CLI.TfsMigration/DevOpsMigrationPlatform.CLI.TfsMigration.csproj'
$ControlPlaneProject = Join-Path $RepoRoot 'src/DevOpsMigrationPlatform.ControlPlaneHost/DevOpsMigrationPlatform.ControlPlaneHost.csproj'
$AgentProject        = Join-Path $RepoRoot 'src/DevOpsMigrationPlatform.MigrationAgent/DevOpsMigrationPlatform.MigrationAgent.csproj'

# ─────────────────────────────────────────────────────────────────────────────
# Helpers
# ─────────────────────────────────────────────────────────────────────────────
function Invoke-Step {
    param([string]$Description, [scriptblock]$Action)
    Write-Host "`n==> $Description" -ForegroundColor Cyan
    & $Action
    if ($LASTEXITCODE -ne 0) {
        Write-Error "Step failed: $Description (exit code $LASTEXITCODE)"
        exit $LASTEXITCODE
    }
}

# ─────────────────────────────────────────────────────────────────────────────
# Step 1: Resolve Version via GitVersion
# ─────────────────────────────────────────────────────────────────────────────
Write-Host "`n==> Resolving version via GitVersion..." -ForegroundColor Cyan

$gitVersionJson = $null

# Prefer global install (used by CI after gittools/actions/gitversion/setup)
$gvCmd = Get-Command 'dotnet-gitversion' -ErrorAction SilentlyContinue
if ($gvCmd) {
    $gitVersionJson = & dotnet-gitversion /output json 2>&1
} else {
    # Fall back to local tool manifest
    dotnet tool restore --verbosity quiet 2>&1 | Out-Null
    if ($LASTEXITCODE -ne 0) {
        Write-Error "dotnet tool restore failed (exit code $LASTEXITCODE). Ensure .config/dotnet-tools.json is present or install GitVersion globally: dotnet tool install --global GitVersion.Tool --version 6.1.0"
        exit 1
    }
    $gitVersionJson = & dotnet tool run dotnet-gitversion -- /output json 2>&1
}

if ($LASTEXITCODE -ne 0) {
    Write-Error @"
GitVersion could not be executed (exit code $LASTEXITCODE).
Output: $gitVersionJson

Install GitVersion globally (use the same version as .config/dotnet-tools.json):
    dotnet tool install --global GitVersion.Tool --version 6.1.0

Or restore the local tool manifest:
    dotnet tool restore
"@
    exit 1
}

# GitVersion may emit INFO/WARN diagnostic lines before the JSON object; extract just the JSON.
# The JSON output is a single line starting with '{' and containing '"SemVer"'.
$jsonLine = $gitVersionJson | Where-Object { $_ -match '^\s*\{' -and $_ -match '"SemVer"' } | Select-Object -First 1

if (-not $jsonLine) {
    Write-Error "Could not locate JSON output from GitVersion. Full output:`n$($gitVersionJson -join "`n")"
    exit 1
}

try {
    $versionInfo = $jsonLine | ConvertFrom-Json
} catch {
    Write-Error "Failed to parse GitVersion JSON output: $_`nRaw JSON line: $jsonLine"
    exit 1
}

$SemVer               = $versionInfo.SemVer
$AssemblySemVer       = $versionInfo.AssemblySemVer
$InformationalVersion = $versionInfo.InformationalVersion

Write-Host "  SemVer:               $SemVer"
Write-Host "  AssemblySemVer:       $AssemblySemVer"
Write-Host "  InformationalVersion: $InformationalVersion"

# Version-specific staging directory
$StagingDir = Join-Path $ArtifactsDir $SemVer
New-Item -ItemType Directory -Path $StagingDir -Force | Out-Null

# MSBuild version properties reused across all steps
$VersionArgs = @(
    "/p:Version=$SemVer",
    "/p:FileVersion=$AssemblySemVer",
    "/p:InformationalVersion=$InformationalVersion"
)

# ─────────────────────────────────────────────────────────────────────────────
# Step 2: Build Solution
# ─────────────────────────────────────────────────────────────────────────────
Invoke-Step 'Building solution' {
    dotnet build $SolutionFile `
        --configuration Release `
        @VersionArgs
}

# ─────────────────────────────────────────────────────────────────────────────
# Step 3: Run Tests
# ─────────────────────────────────────────────────────────────────────────────
Invoke-Step 'Running tests' {
    dotnet test $SolutionFile `
        --no-build `
        --configuration Release `
        --logger 'trx' `
        --logger 'console;verbosity=normal' `
        --results-directory $TestResultsDir
}

# ─────────────────────────────────────────────────────────────────────────────
# Step 4: Publish Components
# ─────────────────────────────────────────────────────────────────────────────
$CliMigrationOut = Join-Path $StagingDir 'cli-migration'
$CliTfsOut       = Join-Path $StagingDir 'cli-tfs'
$ControlPlaneOut = Join-Path $StagingDir 'controlplane'
$AgentOut        = Join-Path $StagingDir 'agent'

Invoke-Step 'Publishing CLI (devopsMigration)' {
    dotnet publish $CliMigrationProject `
        --configuration Release `
        --no-build `
        --output $CliMigrationOut `
        @VersionArgs
}

Invoke-Step 'Publishing CLI (TfsMigration)' {
    dotnet publish $CliTfsProject `
        --configuration Release `
        --no-build `
        --output $CliTfsOut `
        @VersionArgs
}

Invoke-Step 'Publishing Control Plane' {
    dotnet publish $ControlPlaneProject `
        --configuration Release `
        --no-build `
        --output $ControlPlaneOut `
        @VersionArgs
}

Invoke-Step 'Publishing Agent' {
    dotnet publish $AgentProject `
        --configuration Release `
        --no-build `
        --output $AgentOut `
        @VersionArgs
}

# ─────────────────────────────────────────────────────────────────────────────
# Step 5: Package Artefacts
# ─────────────────────────────────────────────────────────────────────────────

# --- CLI Package: MigrationTools-{SemVer}.zip ---
# Structure inside zip: tools/devopsmigration.exe + tools/tfsmigration.exe
Write-Host "`n==> Packaging CLI artefact..." -ForegroundColor Cyan

$CliZipStaging = Join-Path $StagingDir 'cli-zip-staging'
$ToolsDir      = Join-Path $CliZipStaging 'tools'
New-Item -ItemType Directory -Path $ToolsDir -Force | Out-Null

Copy-Item -Path (Join-Path $CliMigrationOut '*') -Destination $ToolsDir -Recurse -Force
Copy-Item -Path (Join-Path $CliTfsOut '*') -Destination $ToolsDir -Recurse -Force

$CliZip = Join-Path $ArtifactsDir "MigrationTools-$SemVer.zip"
Push-Location $CliZipStaging
Compress-Archive -Path 'tools' -DestinationPath $CliZip -Force
Pop-Location
Write-Host "  Created: $CliZip"

# --- Control Plane Package: ControlPlane-{SemVer}.zip ---
Write-Host "`n==> Packaging Control Plane artefact..." -ForegroundColor Cyan
$ControlPlaneZip = Join-Path $ArtifactsDir "ControlPlane-$SemVer.zip"
Compress-Archive -Path (Join-Path $ControlPlaneOut '*') -DestinationPath $ControlPlaneZip -Force
Write-Host "  Created: $ControlPlaneZip"

# --- Agent Package: Agent-{SemVer}.zip ---
Write-Host "`n==> Packaging Agent artefact..." -ForegroundColor Cyan
$AgentZip = Join-Path $ArtifactsDir "Agent-$SemVer.zip"
Compress-Archive -Path (Join-Path $AgentOut '*') -DestinationPath $AgentZip -Force
Write-Host "  Created: $AgentZip"

# ─────────────────────────────────────────────────────────────────────────────
# Summary
# ─────────────────────────────────────────────────────────────────────────────
Write-Host "`n==> Build complete!" -ForegroundColor Green
Write-Host "  Version:    $SemVer"
Write-Host "  Artefacts:"
Get-ChildItem -Path $ArtifactsDir -Filter '*.zip' | ForEach-Object {
    Write-Host "    $($_.Name)"
}
