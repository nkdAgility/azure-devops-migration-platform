#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Single-entry-point build script for the Azure DevOps Migration Platform.

.DESCRIPTION
    Resolves version via GitVersion then executes the requested mode:

      Build       — (default) Compile the solution and run unit tests
                    (TestCategory!=SystemTest). Use this for fast feedback
                    during development and as the first CI step.

      SystemTest  — Run only the slow/integration system tests
                    (TestCategory=SystemTest) against the already-compiled
                    binaries. Run this after Build.

      Package     — Build + unit tests + system tests + publish + zip.
                    Produces distributable artefacts under ./artifacts/.
                    Use for Preview (push to main) and Production (v* tags).

      Start       — Everything in Package, then launches the Aspire AppHost
                    (ControlPlane + MigrationAgent) for local developer
                    simulation of the production topology. Ctrl-C to stop.

    Workflow matrix:
      Canary  (PR)         :  Build  →  SystemTest   (separate steps)
      Preview (main push)  :  Package
      Production (v* / release): Package
      Developer local      :  Start

    Prerequisites:
      - .NET SDK (see global.json)
      - GitVersion.Tool 6.1.0:
            dotnet tool install --global GitVersion.Tool --version 6.1.0
        OR restore from the local tool manifest:
            dotnet tool restore

    Artefact outputs (placed under ./output/):
      - MigrationTools-{SemVer}.zip   — CLI tools (devopsMigration + TfsMigration)
      - ControlPlane-{SemVer}.zip     — Control Plane host
      - Agent-{SemVer}.zip            — Migration Agent worker

.PARAMETER Mode
    Build | SystemTest | Package | Start   (default: Build)

.EXAMPLE
    pwsh ./build.ps1
    pwsh ./build.ps1 -Mode Build
    pwsh ./build.ps1 -Mode SystemTest
    pwsh ./build.ps1 -Mode Package
    pwsh ./build.ps1 -Mode Start
#>
param(
    [ValidateSet('Build', 'SystemTest', 'Package', 'Start')]
    [string]$Mode = 'Build'
)

$ErrorActionPreference = 'Stop'
$ProgressPreference    = 'SilentlyContinue'

$RepoRoot       = $PSScriptRoot
$SolutionFile   = Join-Path $RepoRoot 'DevOpsMigrationPlatform.slnx'
$ArtifactsDir   = Join-Path $RepoRoot 'output'
$TestResultsDir = Join-Path $RepoRoot 'TestResults'

$AppHostProject      = Join-Path $RepoRoot 'src/DevOpsMigrationPlatform.AppHost/DevOpsMigrationPlatform.AppHost.csproj'
$CliMigrationProject = Join-Path $RepoRoot 'src/DevOpsMigrationPlatform.CLI.Migration/DevOpsMigrationPlatform.CLI.Migration.csproj'
$CliTfsProject       = Join-Path $RepoRoot 'src/DevOpsMigrationPlatform.CLI.TfsMigration/DevOpsMigrationPlatform.CLI.TfsMigration.csproj'
$ControlPlaneProject = Join-Path $RepoRoot 'src/DevOpsMigrationPlatform.ControlPlaneHost/DevOpsMigrationPlatform.ControlPlaneHost.csproj'
$AgentProject        = Join-Path $RepoRoot 'src/DevOpsMigrationPlatform.MigrationAgent/DevOpsMigrationPlatform.MigrationAgent.csproj'

Write-Host "`n==> Mode: $Mode" -ForegroundColor Magenta

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
# Version resolution (runs for every mode)
# ─────────────────────────────────────────────────────────────────────────────
function Resolve-GitVersion {
    Write-Host "`n==> Resolving version via GitVersion..." -ForegroundColor Cyan

    $rawOutput = $null

    $ConfigFile = Join-Path $RepoRoot '.build/GitVersion.yml'

    # Prefer global install (set up by gittools/actions/gitversion/setup in CI)
    $gvCmd = Get-Command 'dotnet-gitversion' -ErrorAction SilentlyContinue
    if ($gvCmd) {
        $rawOutput = & dotnet-gitversion /config $ConfigFile /output json 2>&1
    } else {
        # Fall back to local tool manifest
        dotnet tool restore --verbosity quiet 2>&1 | Out-Null
        if ($LASTEXITCODE -ne 0) {
            Write-Error "dotnet tool restore failed (exit $LASTEXITCODE). Ensure .config/dotnet-tools.json is present or install GitVersion globally: dotnet tool install --global GitVersion.Tool --version 6.1.0"
            exit 1
        }
        $rawOutput = & dotnet tool run dotnet-gitversion -- /config $ConfigFile /output json 2>&1
    }

    if ($LASTEXITCODE -ne 0) {
        Write-Error @"
GitVersion could not be executed (exit code $LASTEXITCODE).
Output: $rawOutput

Install GitVersion globally:
    dotnet tool install --global GitVersion.Tool --version 6.1.0
Or restore the local manifest:
    dotnet tool restore
"@
        exit 1
    }

    # GitVersion may emit INFO/WARN diagnostic lines mixed with the JSON.
    # The JSON may be pretty-printed (one key per line) or compact (single line).
    # Collect all lines from the first standalone '{' through the matching closing '}'.
    $inJson = $false; $depth = 0; $jsonLines = @()
    foreach ($line in $rawOutput) {
        if (-not $inJson -and $line -match '^\s*\{') { $inJson = $true }
        if ($inJson) {
            $jsonLines += $line
            $depth += ($line.ToCharArray() | Where-Object { $_ -eq '{' }).Count
            $depth -= ($line.ToCharArray() | Where-Object { $_ -eq '}' }).Count
            if ($depth -le 0) { break }
        }
    }

    $jsonText = $jsonLines -join "`n"
    if (-not $jsonText) {
        Write-Error "Could not locate JSON output from GitVersion. Full output:`n$($rawOutput -join "`n")"
        exit 1
    }

    try {
        return $jsonText | ConvertFrom-Json
    } catch {
        Write-Error "Failed to parse GitVersion JSON: $_`nRaw output:`n$jsonText"
        exit 1
    }
}

# ─────────────────────────────────────────────────────────────────────────────
# Build functions
# ─────────────────────────────────────────────────────────────────────────────
function Invoke-Build {
    param($VersionArgs)
    Invoke-Step 'Building solution' {
        dotnet build $SolutionFile `
            --configuration Release `
            @VersionArgs
    }
}

function Invoke-UnitTests {
    # All tests EXCEPT SystemTest-categorised tests
    Invoke-Step 'Running unit tests (excluding SystemTests)' {
        dotnet test $SolutionFile `
            --no-build `
            --configuration Release `
            --filter 'TestCategory!=SystemTest' `
            --logger 'trx' `
            --logger 'console;verbosity=normal' `
            --results-directory $TestResultsDir
    }
}

function Invoke-SystemTests {
    # Only tests tagged [TestCategory("SystemTest")]
    Invoke-Step 'Running system tests (TestCategory=SystemTest)' {
        dotnet test $SolutionFile `
            --no-build `
            --configuration Release `
            --filter 'TestCategory=SystemTest' `
            --logger 'trx' `
            --logger 'console;verbosity=normal' `
            --results-directory $TestResultsDir
    }
}

function Invoke-Publish {
    param($StagingDir, $VersionArgs)

    $script:CliMigrationOut = Join-Path $StagingDir 'cli-migration'
    $script:CliTfsOut       = Join-Path $StagingDir 'cli-tfs'
    $script:ControlPlaneOut = Join-Path $StagingDir 'controlplane'
    $script:AgentOut        = Join-Path $StagingDir 'agent'

    Invoke-Step 'Publishing CLI (devopsMigration)' {
        dotnet publish $CliMigrationProject `
            --configuration Release `
            --no-build `
            --output $script:CliMigrationOut `
            @VersionArgs
    }

    Invoke-Step 'Publishing CLI (TfsMigration)' {
        dotnet publish $CliTfsProject `
            --configuration Release `
            --no-build `
            --output $script:CliTfsOut `
            @VersionArgs
    }

    Invoke-Step 'Publishing Control Plane' {
        dotnet publish $ControlPlaneProject `
            --configuration Release `
            --no-build `
            --output $script:ControlPlaneOut `
            @VersionArgs
    }

    Invoke-Step 'Publishing Agent' {
        dotnet publish $AgentProject `
            --configuration Release `
            --no-build `
            --output $script:AgentOut `
            @VersionArgs
    }
}

function Invoke-Package {
    param($SemVer, $StagingDir)

    # --- CLI Package: MigrationTools-{SemVer}.zip ---
    # Structure inside zip: tools/  (both CLIs merged flat)
    Write-Host "`n==> Packaging CLI artefact..." -ForegroundColor Cyan
    $CliZipStaging = Join-Path $StagingDir 'cli-zip-staging'
    $ToolsDir      = Join-Path $CliZipStaging 'tools'
    New-Item -ItemType Directory -Path $ToolsDir -Force | Out-Null
    Copy-Item -Path (Join-Path $script:CliMigrationOut '*') -Destination $ToolsDir -Recurse -Force
    Copy-Item -Path (Join-Path $script:CliTfsOut '*') -Destination $ToolsDir -Recurse -Force
    $CliZip = Join-Path $ArtifactsDir "MigrationTools-$SemVer.zip"
    Push-Location $CliZipStaging
    Compress-Archive -Path 'tools' -DestinationPath $CliZip -Force
    Pop-Location
    Write-Host "  Created: $CliZip"

    # --- Control Plane Package ---
    Write-Host "`n==> Packaging Control Plane artefact..." -ForegroundColor Cyan
    $ControlPlaneZip = Join-Path $ArtifactsDir "ControlPlane-$SemVer.zip"
    Compress-Archive -Path (Join-Path $script:ControlPlaneOut '*') -DestinationPath $ControlPlaneZip -Force
    Write-Host "  Created: $ControlPlaneZip"

    # --- Agent Package ---
    Write-Host "`n==> Packaging Agent artefact..." -ForegroundColor Cyan
    $AgentZip = Join-Path $ArtifactsDir "Agent-$SemVer.zip"
    Compress-Archive -Path (Join-Path $script:AgentOut '*') -DestinationPath $AgentZip -Force
    Write-Host "  Created: $AgentZip"
}

function Start-AppHost {
    Write-Host "`n==> Starting Aspire AppHost (ControlPlane + MigrationAgent)..." -ForegroundColor Cyan
    Write-Host "    Press Ctrl-C to stop." -ForegroundColor Yellow
    # dotnet run performs its own build pass against the project references;
    # running after Package ensures the Release binaries are warm.
    dotnet run --project $AppHostProject --configuration Release
    # Non-zero exit (e.g. Ctrl-C) is expected and not treated as a failure here.
}

# ─────────────────────────────────────────────────────────────────────────────
# Orchestration
# ─────────────────────────────────────────────────────────────────────────────
$versionInfo = Resolve-GitVersion

$SemVer               = $versionInfo.SemVer
$AssemblySemVer       = $versionInfo.AssemblySemVer
$InformationalVersion = $versionInfo.InformationalVersion

Write-Host "  SemVer:               $SemVer"
Write-Host "  AssemblySemVer:       $AssemblySemVer"
Write-Host "  InformationalVersion: $InformationalVersion"

$VersionArgs = @(
    "/p:Version=$SemVer",
    "/p:FileVersion=$AssemblySemVer",
    "/p:InformationalVersion=$InformationalVersion"
)

$StagingDir = Join-Path $ArtifactsDir $SemVer
New-Item -ItemType Directory -Path $StagingDir -Force | Out-Null

switch ($Mode) {

    'Build' {
        # ── Canary step 1: compile + unit tests ──────────────────────────────
        Invoke-Build    -VersionArgs $VersionArgs
        Invoke-UnitTests

        Write-Host "`n==> Build complete!" -ForegroundColor Green
        Write-Host "  Version: $SemVer"
    }

    'SystemTest' {
        # ── Canary step 2: system tests only (binaries compiled by Build) ────
        Invoke-SystemTests

        Write-Host "`n==> System tests complete!" -ForegroundColor Green
    }

    'Package' {
        # ── Preview / Production: full pipeline ──────────────────────────────
        Invoke-Build       -VersionArgs $VersionArgs
        Invoke-UnitTests
        Invoke-SystemTests
        Invoke-Publish     -StagingDir $StagingDir -VersionArgs $VersionArgs
        Invoke-Package     -SemVer $SemVer -StagingDir $StagingDir

        Write-Host "`n==> Package complete!" -ForegroundColor Green
        Write-Host "  Version:    $SemVer"
        Write-Host "  Artefacts:"
        Get-ChildItem -Path $ArtifactsDir -Filter '*.zip' | ForEach-Object {
            Write-Host "    $($_.Name)"
        }
    }

    'Start' {
        # ── Developer local: full pipeline + launch Aspire ───────────────────
        Invoke-Build       -VersionArgs $VersionArgs
        Invoke-UnitTests
        Invoke-SystemTests
        Invoke-Publish     -StagingDir $StagingDir -VersionArgs $VersionArgs
        Invoke-Package     -SemVer $SemVer -StagingDir $StagingDir
        Start-AppHost
    }
}
