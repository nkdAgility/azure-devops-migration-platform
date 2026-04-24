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

      SystemTest           — Run all system tests in order: first
                             SystemTest_Simulated, then SystemTest_Live,
                             against the already-compiled binaries.

      SystemTest_Simulated — Run only simulated/offline system tests
                             (TestCategory=SystemTest_Simulated) against the
                             already-compiled binaries.

      SystemTest_Live      — Run only live/online system tests that require
                             real Azure DevOps credentials
                             (TestCategory=SystemTest_Live) against the
                             already-compiled binaries.

      Package     — Publish + zip only against the already-compiled binaries.
                    Produces distributable artefacts under ./output/.

      Full        — Build + Test + SystemTest_Simulated + SystemTest_Live +
                    Package in sequence.
                    Use for Preview (push to main) and Production releases.

      Start       — Install (build + unit test + publish + install to versioned
                    folder + update 'current' junction), then launches the
                    Aspire AppHost (ControlPlane + MigrationAgent) so you can
                    run 'devopsmigrationdev' against it. Ctrl-C to stop.

      Install     — Build + Test (unit only) + publish for the current
                    platform, then installs to
                    %USERPROFILE%\source\Tools\MigrationPlatform\{version}\
                    and updates a 'current' junction so that the
                    'devopsmigrationdev' alias always runs the latest build.
                    Shim is written to %USERPROFILE%\.dotnet\tools\ which is
                    already on PATH for any machine with the .NET SDK.

    Workflow matrix:
      PR                   :  Build  →  Test  →  SystemTest_Simulated  →  SystemTest_Live   (separate steps)
      Preview (main push)  :  Build  →  Test  →  SystemTest_Simulated  →  SystemTest_Live  →  Package
      Production (release) :  Build  →  Test  →  SystemTest_Simulated  →  SystemTest_Live  →  Package
      Developer local      :  Install  (or Start to also launch Aspire)

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
    Build | Test | SystemTest | SystemTest_Simulated | SystemTest_Live | Package | Full | Start | Install   (default: Full)

.PARAMETER Version
    Override the version string instead of resolving via GitVersion.
    When set, SemVer / AssemblySemVer / InformationalVersion are all
    set to this value and GitVersion is not invoked.

.EXAMPLE
    pwsh ./build.ps1                  # Full pipeline
    pwsh ./build.ps1 -Mode Build
    pwsh ./build.ps1 -Mode Test
    pwsh ./build.ps1 -Mode SystemTest
    pwsh ./build.ps1 -Mode SystemTest_Simulated
    pwsh ./build.ps1 -Mode SystemTest_Live
    pwsh ./build.ps1 -Mode Package
    pwsh ./build.ps1 -Mode Full
    pwsh ./build.ps1 -Mode Start
    pwsh ./build.ps1 -Mode Install
    pwsh ./build.ps1 -Version 16.9.3  # Override version
#>
param(
    [ValidateSet('Build', 'Test', 'SystemTest', 'SystemTest_Simulated', 'SystemTest_Live', 'Package', 'Full', 'Start', 'Install')]
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
$AllRids = @('win-x64', 'win-arm64', 'linux-x64', 'osx-x64', 'osx-arm64')

# ─────────────────────────────────────────────────────────────────────────────
# Helpers
# ─────────────────────────────────────────────────────────────────────────────
$script:StepTimings = [System.Collections.Generic.List[PSCustomObject]]::new()
$script:BuildStart  = [System.Diagnostics.Stopwatch]::StartNew()

function Write-Banner {
    param([string]$SemVer, [string]$Mode)
    $width = 60
    $line  = '═' * $width
    Write-Host ""
    Write-Host $line -ForegroundColor DarkCyan
    Write-Host ('  Azure DevOps Migration Platform  —  build.ps1') -ForegroundColor Cyan
    Write-Host ('  Version : {0}' -f $SemVer) -ForegroundColor White
    Write-Host ('  Mode    : {0}' -f $Mode) -ForegroundColor White
    Write-Host $line -ForegroundColor DarkCyan
    Write-Host ""
}

function Invoke-Step {
    param([string]$Description, [scriptblock]$Action)
    Write-Host "`n==> $Description" -ForegroundColor Cyan
    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    & $Action
    $sw.Stop()
    $script:StepTimings.Add([PSCustomObject]@{ Step = $Description; Elapsed = $sw.Elapsed })
    if ($LASTEXITCODE -ne 0) {
        Write-Error "Step failed: $Description (exit code $LASTEXITCODE)"
        exit $LASTEXITCODE
    }
}

function Write-TestSummary {
    # Parse .trx files from TestResults/ to show per-assembly test counts.
    $trxFiles = Get-ChildItem -LiteralPath $TestResultsDir -Filter '*.trx' -ErrorAction SilentlyContinue
    if (-not $trxFiles -or $trxFiles.Count -eq 0) { return }

    Write-Host ""
    Write-Host ('─' * 72) -ForegroundColor DarkGray
    Write-Host '  Test Summary' -ForegroundColor White
    Write-Host ('─' * 72) -ForegroundColor DarkGray

    $totalPassed = 0; $totalFailed = 0; $totalSkipped = 0; $totalTests = 0

    foreach ($trx in $trxFiles) {
        [xml]$xml = Get-Content -LiteralPath $trx.FullName -Raw
        $ns = @{ t = 'http://microsoft.com/schemas/VisualStudio/TeamTest/2010' }
        $counters = Select-Xml -Xml $xml -XPath '//t:ResultSummary/t:Counters' -Namespace $ns |
                    Select-Object -ExpandProperty Node

        if (-not $counters) { continue }

        $passed  = [int]$counters.passed
        $failed  = [int]$counters.failed
        $skipped = [int]$counters.notExecuted
        $total   = [int]$counters.total

        $totalPassed  += $passed
        $totalFailed  += $failed
        $totalSkipped += $skipped
        $totalTests   += $total

        # Derive assembly name from the .trx storage attribute or filename
        $firstResult = Select-Xml -Xml $xml -XPath '//t:Results/t:UnitTestResult[1]' -Namespace $ns |
                       Select-Object -ExpandProperty Node
        $assembly = if ($firstResult -and $firstResult.testName) {
            $firstResult.testName -replace '\..*', ''
        } else {
            [System.IO.Path]::GetFileNameWithoutExtension($trx.Name)
        }
        # Try to get a cleaner name from TestDefinitions
        $defNode = Select-Xml -Xml $xml -XPath '//t:TestDefinitions/t:UnitTest[1]/t:TestMethod' -Namespace $ns |
                   Select-Object -ExpandProperty Node
        if ($defNode -and $defNode.className) {
            $assembly = ($defNode.className -split ',')[0] -replace '\.Tests\..*|\.Test\..*', '.Tests'
        }

        $statusIcon = if ($failed -gt 0) { '✗' } else { '✓' }
        $statusColor = if ($failed -gt 0) { 'Red' } else { 'Green' }
        $line = '  {0} {1,-42} {2,5} passed  {3,3} failed  {4,3} skipped  {5,5} total' -f $statusIcon, $assembly, $passed, $failed, $skipped, $total
        Write-Host $line -ForegroundColor $statusColor
    }

    Write-Host ('─' * 72) -ForegroundColor DarkGray
    $summaryIcon = if ($totalFailed -gt 0) { '✗' } else { '✓' }
    $summaryColor = if ($totalFailed -gt 0) { 'Red' } else { 'Green' }
    $summaryLine = '  {0} {1,-42} {2,5} passed  {3,3} failed  {4,3} skipped  {5,5} total' -f $summaryIcon, 'ALL TESTS', $totalPassed, $totalFailed, $totalSkipped, $totalTests
    Write-Host $summaryLine -ForegroundColor $summaryColor
    Write-Host ('─' * 72) -ForegroundColor DarkGray
    Write-Host ''
}

function Write-BuildSummary {
    $script:BuildStart.Stop()
    $total = $script:BuildStart.Elapsed

    # Show test counts first (if any tests were run)
    Write-TestSummary

    Write-Host ""
    Write-Host ('─' * 72) -ForegroundColor DarkGray
    Write-Host '  Build Summary' -ForegroundColor White
    Write-Host ('─' * 72) -ForegroundColor DarkGray

    foreach ($entry in $script:StepTimings) {
        $t = $entry.Elapsed
        $formatted = if ($t.TotalMinutes -ge 1) {
            '{0}m {1:D2}s' -f [int]$t.TotalMinutes, $t.Seconds
        } else {
            '{0:D2}s {1:D3}ms' -f $t.Seconds, $t.Milliseconds
        }
        Write-Host ('  {0,-55} {1,10}' -f $entry.Step, $formatted) -ForegroundColor Gray
    }

    Write-Host ('─' * 72) -ForegroundColor DarkGray
    $tf = if ($total.TotalMinutes -ge 1) {
        '{0}m {1:D2}s' -f [int]$total.TotalMinutes, $total.Seconds
    } else {
        '{0:D2}s {1:D3}ms' -f $total.Seconds, $total.Milliseconds
    }
    Write-Host ('  {0,-55} {1,10}' -f 'TOTAL', $tf) -ForegroundColor White
    Write-Host ('─' * 72) -ForegroundColor DarkGray
    Write-Host ''
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
    # Clear stale .trx files so the summary only reflects the current run
    if (Test-Path $TestResultsDir) {
        Get-ChildItem -LiteralPath $TestResultsDir -Filter '*.trx' -ErrorAction SilentlyContinue |
            Remove-Item -Force -ErrorAction SilentlyContinue
    }
    # All tests EXCEPT SystemTest-categorised tests (including sub-categories)
    Invoke-Step 'Running unit tests (excluding SystemTests)' {
        dotnet test $SolutionFile `
            --no-build `
            --configuration Release `
            --filter 'TestCategory!=SystemTest&TestCategory!=SystemTest_Simulated&TestCategory!=SystemTest_Live' `
            --logger 'trx' `
            --logger 'console;verbosity=normal' `
            --results-directory $TestResultsDir
    }
}

function Invoke-SimulatedSystemTests {
    # Only tests tagged [TestCategory("SystemTest_Simulated")]
    Invoke-Step 'Running simulated system tests (TestCategory=SystemTest_Simulated)' {
        dotnet test $SolutionFile `
            --no-build `
            --configuration Release `
            --filter 'TestCategory=SystemTest_Simulated' `
            --logger 'trx' `
            --logger 'console;verbosity=normal' `
            --results-directory $TestResultsDir
    }
}

function Invoke-LiveSystemTests {
    # Only tests tagged [TestCategory("SystemTest_Live")]
    Invoke-Step 'Running live system tests (TestCategory=SystemTest_Live)' {
        dotnet test $SolutionFile `
            --no-build `
            --configuration Release `
            --filter 'TestCategory=SystemTest_Live' `
            --logger 'trx' `
            --logger 'console;verbosity=normal' `
            --results-directory $TestResultsDir
    }
}

function Invoke-SystemTests {
    # Run simulated tests first; only proceed to live tests if simulated pass.
    # This minimises time-to-failure when simulated tests catch an issue early.
    Invoke-SimulatedSystemTests
    Invoke-LiveSystemTests
}

function Invoke-Publish {
    param($StagingDir, $VersionArgs, [string[]]$TargetRids = $script:AllRids)

    $script:CliMigrationOutByRid = @{}
    $script:ControlPlaneOutByRid = @{}
    $script:AgentOutByRid        = @{}

    foreach ($rid in $TargetRids) {
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
    param($SemVer, $StagingDir, [string[]]$TargetRids = $script:AllRids)

    foreach ($rid in $TargetRids) {
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
    param([string]$InstallPath)

    Write-Host "`n==> Starting Aspire AppHost from installed location: $InstallPath" -ForegroundColor Cyan
    Write-Host "    Press Ctrl-C to stop." -ForegroundColor Yellow

    $env:MIGRATION_INSTALL_PATH = $InstallPath
    try {
        # dotnet run compiles the AppHost from source, but the AppHost detects
        # MIGRATION_INSTALL_PATH and launches the installed ControlPlane and
        # MigrationAgent executables via AddExecutable instead of AddProject.
        dotnet run --project $AppHostProject --configuration Release
        # Non-zero exit (e.g. Ctrl-C) is expected and not treated as a failure here.
    } finally {
        Remove-Item Env:\MIGRATION_INSTALL_PATH -ErrorAction SilentlyContinue
    }
}

# ─────────────────────────────────────────────────────────────────────────────
# Install helpers
# ─────────────────────────────────────────────────────────────────────────────
function Get-CurrentRid {
    if ($IsWindows) {
        $arch = [System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture
        if ($arch -eq [System.Runtime.InteropServices.Architecture]::Arm64) { return 'win-arm64' }
        return 'win-x64'
    } elseif ($IsMacOS) {
        $arch = [System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture
        if ($arch -eq [System.Runtime.InteropServices.Architecture]::Arm64) { return 'osx-arm64' }
        return 'osx-x64'
    } else {
        return 'linux-x64'
    }
}

function Invoke-Install {
    param([string]$SemVer)

    $rid        = Get-CurrentRid
    $displayRid = $rid -replace '^osx-', 'macos-'
    $zip        = Join-Path $ArtifactsDir "MigrationTools-$SemVer-$displayRid.zip"

    if (-not (Test-Path $zip)) {
        Write-Error "Package not found: $zip`nRun './build.ps1 -Mode Package' (or 'Install') to build the package first."
        exit 1
    }

    # ── Install root: %USERPROFILE%\source\Tools\MigrationPlatform\ ──────────
    $installRoot  = Join-Path $env:USERPROFILE 'source\Tools\MigrationPlatform'
    $versionedDir = Join-Path $installRoot $SemVer
    $currentDir   = Join-Path $installRoot 'current'

    Write-Host "`n==> Installing $SemVer [$rid] from package to $versionedDir" -ForegroundColor Cyan
    Write-Host "  Source: $zip"

    # Remove any previous install for this version then extract the full package.
    # The zip contains: CLI at root, ControlPlane/, MigrationAgent/, TfsMigration/ (win-x64).
    if (Test-Path $versionedDir) {
        Remove-Item $versionedDir -Recurse -Force
    }
    New-Item -ItemType Directory -Path $versionedDir -Force | Out-Null
    Expand-Archive -Path $zip -DestinationPath $versionedDir -Force

    # ── Update 'current' junction ────────────────────────────────────────────
    if (Test-Path $currentDir) {
        $existing = Get-Item $currentDir -Force
        if ($existing.LinkType -in @('Junction', 'SymbolicLink')) {
            [System.IO.Directory]::Delete($currentDir)   # removes link only, not target
        } else {
            Remove-Item $currentDir -Recurse -Force
        }
    }
    New-Item -ItemType Junction -Path $currentDir -Target $versionedDir | Out-Null

    # ── Write shim to %USERPROFILE%\.dotnet\tools\ ───────────────────────────
    # That directory is added to PATH automatically by the .NET SDK installer.
    $shimDir = Join-Path $env:USERPROFILE '.dotnet\tools'
    New-Item -ItemType Directory -Path $shimDir -Force | Out-Null

    # CMD shim — works in cmd.exe, PowerShell, and Windows Terminal
    $cmdShim = Join-Path $shimDir 'devopsmigrationdev.cmd'
    "@echo off`r`n""%USERPROFILE%\source\Tools\MigrationPlatform\current\devopsmigration.exe"" %*" |
        Set-Content -Path $cmdShim -Encoding ASCII

    # PS1 shim — explicit PowerShell invocation path
    $ps1Shim = Join-Path $shimDir 'devopsmigrationdev.ps1'
    "& (Join-Path `$env:USERPROFILE 'source\Tools\MigrationPlatform\current\devopsmigration.exe') @args" |
        Set-Content -Path $ps1Shim

    Write-Host "`n==> Install complete!" -ForegroundColor Green
    Write-Host "  Version:       $SemVer"
    Write-Host "  Installed to:  $versionedDir"
    Write-Host "  Contains:      CLI (root), ControlPlane/, MigrationAgent/, TfsMigration/ (win-x64)"
    Write-Host "  Current link:  $currentDir  ->  $versionedDir"
    Write-Host "  Shim (.cmd):   $cmdShim"
    Write-Host "  Shim (.ps1):   $ps1Shim"
    Write-Host "`n  Run from any terminal: devopsmigrationdev --help" -ForegroundColor Cyan

    # Return the versioned dir so callers can use it (e.g. Start mode)
    return $versionedDir
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

Write-Banner -SemVer $SemVer -Mode $Mode

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
        Write-BuildSummary
    }

    'Test' {
        # ── Unit tests only (requires prior Build) ───────────────────────────
        Invoke-UnitTests

        Write-Host "`n==> Unit tests complete!" -ForegroundColor Green
        Write-BuildSummary
    }

    'SystemTest' {
        # ── Simulated system tests then live system tests (requires prior Build) ─
        Invoke-SystemTests

        Write-Host "`n==> System tests complete!" -ForegroundColor Green
        Write-BuildSummary
    }

    'SystemTest_Simulated' {
        # ── Simulated system tests only (requires prior Build) ────────────────
        Invoke-SimulatedSystemTests

        Write-Host "`n==> Simulated system tests complete!" -ForegroundColor Green
        Write-BuildSummary
    }

    'SystemTest_Live' {
        # ── Live system tests only (requires prior Build) ─────────────────────
        Invoke-LiveSystemTests

        Write-Host "`n==> Live system tests complete!" -ForegroundColor Green
        Write-BuildSummary
    }

    'Package' {
        # ── Publish + zip only (requires prior Build) ────────────────────────
        Invoke-Publish -StagingDir $StagingDir -VersionArgs $VersionArgs
        Invoke-Package -SemVer $SemVer -StagingDir $StagingDir -TargetRids $AllRids

        Write-Host "`n==> Package complete!" -ForegroundColor Green
        Write-Host "  Version:    $SemVer"
        Write-Host "  Artefacts:"
        Get-ChildItem -Path $ArtifactsDir -Filter '*.zip' | ForEach-Object {
            Write-Host "    $($_.Name)"
        }
        Write-BuildSummary
    }

    'Full' {
        # ── Everything: Build + Test + SystemTest_Simulated + SystemTest_Live + Package ──
        Invoke-Build       -VersionArgs $VersionArgs
        Invoke-UnitTests
        Invoke-SimulatedSystemTests
        Invoke-LiveSystemTests
        Invoke-Publish     -StagingDir $StagingDir -VersionArgs $VersionArgs
        Invoke-Package     -SemVer $SemVer -StagingDir $StagingDir -TargetRids $AllRids

        Write-Host "`n==> Full pipeline complete!" -ForegroundColor Green
        Write-Host "  Version:    $SemVer"
        Write-Host "  Artefacts:"
        Get-ChildItem -Path $ArtifactsDir -Filter '*.zip' | ForEach-Object {
            Write-Host "    $($_.Name)"
        }
        Write-BuildSummary
    }

    'Start' {
        # ── Build + unit tests + package (this platform only) + install, then launch Aspire ──
        # Aspire uses MIGRATION_INSTALL_PATH to launch the installed ControlPlane and
        # MigrationAgent executables instead of running them from build output.
        # After this you can run 'devopsmigrationdev' against the running services.
        $localRid = Get-CurrentRid
        Invoke-Build   -VersionArgs $VersionArgs
        Invoke-UnitTests
        Invoke-SimulatedSystemTests
        Invoke-LiveSystemTests
        Invoke-Publish -StagingDir $StagingDir -VersionArgs $VersionArgs -TargetRids @($localRid)
        Invoke-Package -SemVer $SemVer -StagingDir $StagingDir -TargetRids @($localRid)
        $installedDir = Invoke-Install -SemVer $SemVer
        Write-BuildSummary
        Start-AppHost -InstallPath $installedDir
    }

    'Install' {
        # ── Build + unit tests + package (this platform only) + extract package to install dir ─
        # The zip contains the full layout: CLI at root, ControlPlane/, MigrationAgent/,
        # TfsMigration/ (win-x64 only). Invoke-Install locates the zip and extracts it.
        $localRid = Get-CurrentRid
        Invoke-Build   -VersionArgs $VersionArgs
        Invoke-UnitTests
        Invoke-SimulatedSystemTests
        Invoke-LiveSystemTests
        Invoke-Publish -StagingDir $StagingDir -VersionArgs $VersionArgs -TargetRids @($localRid)
        Invoke-Package -SemVer $SemVer -StagingDir $StagingDir -TargetRids @($localRid)
        Invoke-Install -SemVer $SemVer
        Write-BuildSummary
    }
}
