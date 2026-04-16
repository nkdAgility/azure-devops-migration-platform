#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Single-entry-point build script for the Azure DevOps Migration Platform.

.DESCRIPTION
    Resolves version via GitVersion then executes the requested mode:

      Build       — (default) Compile the solution only. Use as the first step
                    before running any tests.

      Test        — Run unit tests (TestCategory!=SystemTest) against the
                    already-compiled binaries.

      SystemTest  — Run only the slow/integration system tests
                    (TestCategory=SystemTest) against the already-compiled
                    binaries.

      Package     — Publish + zip only against the already-compiled binaries.
                    Produces distributable artefacts under ./output/.

      Full        — Build + Test + SystemTest + Package in sequence.
                    Use for Preview (push to main) and Production releases.

      Start       — Everything in Full, then launches the Aspire AppHost
                    (ControlPlane + MigrationAgent) for local developer
                    simulation of the production topology. Ctrl-C to stop.

    Workflow matrix:
      PR                   :  Build  →  Test  →  SystemTest   (separate steps)
      Preview (main push)  :  Build  →  Test  →  SystemTest  →  Package
      Production (release) :  Build  →  Test  →  SystemTest  →  Package
      Developer local      :  Full  (or Start to also launch Aspire)

    Prerequisites:
      - .NET SDK (see global.json)
      - GitVersion.Tool 6.1.0:
            dotnet tool install --global GitVersion.Tool --version 6.1.0
        OR restore from the local tool manifest:
            dotnet tool restore

    Artefact outputs (placed under ./output/, one zip per RID):
      - MigrationTools-{SemVer}-{rid}.zip

    Each zip contains:
      /                  — devopsMigration CLI (root)
      /ControlPlane/     — Control Plane host
      /MigrationAgent/   — Migration Agent worker
      /TfsMigration/     — TFS CLI subprocess (win-x64 only)

    RIDs produced: win-x64, win-arm64, linux-x64, osx-x64, osx-arm64

.PARAMETER Mode
    Build | Test | SystemTest | Package | Full | Start   (default: Full)

.PARAMETER Version
    Override the version string instead of resolving via GitVersion.
    When set, SemVer / AssemblySemVer / InformationalVersion are all
    set to this value and GitVersion is not invoked.

.EXAMPLE
    pwsh ./build.ps1                  # Full pipeline
    pwsh ./build.ps1 -Mode Build
    pwsh ./build.ps1 -Mode Test
    pwsh ./build.ps1 -Mode SystemTest
    pwsh ./build.ps1 -Mode Package
    pwsh ./build.ps1 -Mode Full
    pwsh ./build.ps1 -Mode Start
    pwsh ./build.ps1 -Version 16.9.3  # Override version
#>
param(
    [ValidateSet('Build', 'Test', 'SystemTest', 'Package', 'Full', 'Start')]
    [string]$Mode = 'Full',

    [string]$Version
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

# Runtime identifiers for per-platform publishing.
# Only win-x64 gets the tfsmigration/ subfolder; TfsMigration (net481) is Windows-only.
$Rids = @('win-x64', 'win-arm64', 'linux-x64', 'osx-x64', 'osx-arm64')

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

    # When running in CI after gittools/actions/gitversion/execute the action
    # exports all variables as environment variables (GitVersion_SemVer, etc.).
    # Reuse them directly to avoid running the tool a second (or third) time.
    if ($env:GitVersion_SemVer) {
        Write-Host "  Using version from CI environment: $($env:GitVersion_SemVer)" -ForegroundColor DarkCyan
        return [PSCustomObject]@{
            SemVer               = $env:GitVersion_SemVer
            AssemblySemVer       = $env:GitVersion_AssemblySemVer
            InformationalVersion = $env:GitVersion_InformationalVersion
        }
    }

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

    $script:CliMigrationOutByRid = @{}
    $script:ControlPlaneOutByRid = @{}
    $script:AgentOutByRid        = @{}

    foreach ($rid in $Rids) {
        # ── CLI (devopsMigration) ────────────────────────────────────────────
        $ridCliOut = Join-Path $StagingDir "cli-migration-$rid"
        $script:CliMigrationOutByRid[$rid] = $ridCliOut
        Write-Host "`n==> Publishing CLI (devopsMigration) [$rid]" -ForegroundColor Cyan
        dotnet restore $CliMigrationProject -r $rid --verbosity quiet
        if ($LASTEXITCODE -ne 0) { Write-Error "Restore failed: CLI [$rid] (exit $LASTEXITCODE)"; exit $LASTEXITCODE }
        dotnet publish $CliMigrationProject `
            --configuration Release `
            --no-restore `
            --no-self-contained `
            -r $rid `
            --output $ridCliOut `
            @VersionArgs
        if ($LASTEXITCODE -ne 0) { Write-Error "Publish failed: CLI [$rid] (exit $LASTEXITCODE)"; exit $LASTEXITCODE }

        # ── Control Plane ────────────────────────────────────────────────────
        $ridCpOut = Join-Path $StagingDir "controlplane-$rid"
        $script:ControlPlaneOutByRid[$rid] = $ridCpOut
        Write-Host "`n==> Publishing Control Plane [$rid]" -ForegroundColor Cyan
        dotnet restore $ControlPlaneProject -r $rid --verbosity quiet
        if ($LASTEXITCODE -ne 0) { Write-Error "Restore failed: ControlPlane [$rid] (exit $LASTEXITCODE)"; exit $LASTEXITCODE }
        dotnet publish $ControlPlaneProject `
            --configuration Release `
            --no-restore `
            --no-self-contained `
            -r $rid `
            --output $ridCpOut `
            @VersionArgs
        if ($LASTEXITCODE -ne 0) { Write-Error "Publish failed: ControlPlane [$rid] (exit $LASTEXITCODE)"; exit $LASTEXITCODE }

        # ── Agent ────────────────────────────────────────────────────────────
        $ridAgentOut = Join-Path $StagingDir "agent-$rid"
        $script:AgentOutByRid[$rid] = $ridAgentOut
        Write-Host "`n==> Publishing Agent [$rid]" -ForegroundColor Cyan
        dotnet restore $AgentProject -r $rid --verbosity quiet
        if ($LASTEXITCODE -ne 0) { Write-Error "Restore failed: Agent [$rid] (exit $LASTEXITCODE)"; exit $LASTEXITCODE }
        dotnet publish $AgentProject `
            --configuration Release `
            --no-restore `
            --no-self-contained `
            -r $rid `
            --output $ridAgentOut `
            @VersionArgs
        if ($LASTEXITCODE -ne 0) { Write-Error "Publish failed: Agent [$rid] (exit $LASTEXITCODE)"; exit $LASTEXITCODE }
    }

    # TfsMigration — win-x64 only (net481 is Windows-only, no RID flag needed)
    $script:CliTfsOut = Join-Path $StagingDir 'cli-tfs-win-x64'
    Invoke-Step 'Publishing TfsCLI (tfsmigration) [win-x64]' {
        dotnet publish $CliTfsProject `
            --configuration Release `
            --no-build `
            --output $script:CliTfsOut `
            @VersionArgs
    }
}

function Invoke-Package {
    param($SemVer, $StagingDir)

    foreach ($rid in $Rids) {
        Write-Host "`n==> Packaging MigrationTools [$rid]..." -ForegroundColor Cyan
        $zipStaging = Join-Path $StagingDir "zip-staging-$rid"
        New-Item -ItemType Directory -Path $zipStaging -Force | Out-Null

        # ── CLI in root ──────────────────────────────────────────────────────
        Copy-Item -Path (Join-Path $script:CliMigrationOutByRid[$rid] '*') -Destination $zipStaging -Recurse -Force

        # ── ControlPlane subfolder ───────────────────────────────────────────
        $cpSubDir = Join-Path $zipStaging 'ControlPlane'
        New-Item -ItemType Directory -Path $cpSubDir -Force | Out-Null
        Copy-Item -Path (Join-Path $script:ControlPlaneOutByRid[$rid] '*') -Destination $cpSubDir -Recurse -Force

        # ── MigrationAgent subfolder ─────────────────────────────────────────
        $agentSubDir = Join-Path $zipStaging 'MigrationAgent'
        New-Item -ItemType Directory -Path $agentSubDir -Force | Out-Null
        Copy-Item -Path (Join-Path $script:AgentOutByRid[$rid] '*') -Destination $agentSubDir -Recurse -Force

        # ── TfsMigration subfolder (win-x64 only) ───────────────────────────
        # net481 subprocess; other RIDs cannot run it and TfsExportRunner.RunAsync()
        # will reject non-Windows early.
        if ($rid -eq 'win-x64') {
            $tfsSubDir = Join-Path $zipStaging 'TfsMigration'
            New-Item -ItemType Directory -Path $tfsSubDir -Force | Out-Null
            Copy-Item -Path (Join-Path $script:CliTfsOut '*') -Destination $tfsSubDir -Recurse -Force
        }

        $displayRid = $rid -replace '^osx-', 'macos-'
        $zip = Join-Path $ArtifactsDir "MigrationTools-$SemVer-$displayRid.zip"
        Push-Location $zipStaging
        Compress-Archive -Path '*' -DestinationPath $zip -Force
        Pop-Location
        Write-Host "  Created: $zip"
    }
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
if ($Version) {
    Write-Host "`n==> Using explicit version override: $Version" -ForegroundColor Cyan
    $SemVer               = $Version
    $AssemblySemVer       = $Version
    $InformationalVersion = $Version
} else {
    $versionInfo = Resolve-GitVersion

    $SemVer               = $versionInfo.SemVer
    $AssemblySemVer       = $versionInfo.AssemblySemVer
    $InformationalVersion = $versionInfo.InformationalVersion
}

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
        # ── Compile only ─────────────────────────────────────────────────────
        Invoke-Build -VersionArgs $VersionArgs

        Write-Host "`n==> Build complete!" -ForegroundColor Green
        Write-Host "  Version: $SemVer"
    }

    'Test' {
        # ── Unit tests only (requires prior Build) ───────────────────────────
        Invoke-UnitTests

        Write-Host "`n==> Unit tests complete!" -ForegroundColor Green
    }

    'SystemTest' {
        # ── System tests only (requires prior Build) ─────────────────────────
        Invoke-SystemTests

        Write-Host "`n==> System tests complete!" -ForegroundColor Green
    }

    'Package' {
        # ── Publish + zip only (requires prior Build) ────────────────────────
        Invoke-Publish -StagingDir $StagingDir -VersionArgs $VersionArgs
        Invoke-Package -SemVer $SemVer -StagingDir $StagingDir

        Write-Host "`n==> Package complete!" -ForegroundColor Green
        Write-Host "  Version:    $SemVer"
        Write-Host "  Artefacts:"
        Get-ChildItem -Path $ArtifactsDir -Filter '*.zip' | ForEach-Object {
            Write-Host "    $($_.Name)"
        }
    }

    'Full' {
        # ── Everything: Build + Test + SystemTest + Package ───────────────────
        Invoke-Build       -VersionArgs $VersionArgs
        Invoke-UnitTests
        Invoke-SystemTests
        Invoke-Publish     -StagingDir $StagingDir -VersionArgs $VersionArgs
        Invoke-Package     -SemVer $SemVer -StagingDir $StagingDir

        Write-Host "`n==> Full pipeline complete!" -ForegroundColor Green
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
