#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Single-entry-point build script for the Azure DevOps Migration Platform.

.DESCRIPTION
    Resolves version via GitVersion then executes the requested mode:

      Build       — (default) Compile the solution only. Use as the first step
                    before running any tests.

      Test        — Run unit tests only (TestCategory=UnitTests) against the
                    already-compiled binaries.

      CodeTest    — Run all in-process tests (TestCategory=CodeTest): Unit + Domain + Integration.
                    No system required. Equivalent to Test + DomainTest + IntegrationTest.

    SystemTest           — Run all system tests in order: first
                     SystemTest_Smoke, then SystemTest_Simulated, then SystemTest_Live,
                      then remaining SystemTest tests that are not
                      Smoke, Simulated, or Live, against the already-compiled
                      binaries.

      DomainTest  — Run domain tests only (TestCategory=DomainTests) against the
                    already-compiled binaries.

      IntegrationTest — Run integration tests only (TestCategory=IntegrationTests) against the
                    already-compiled binaries.

      SystemTest_Smoke     — Run only smoke startup system tests
                             (TestCategory=SystemTest_Smoke) against the
                             already-compiled binaries.

      SystemTest_Simulated — Run only simulated/offline system tests
                             (TestCategory=SystemTest_Simulated) against the
                             already-compiled binaries.

      SystemTest_Live      — Run only live/online system tests that require
                             real Azure DevOps credentials
                             (TestCategory=SystemTest_Live) against the
                             already-compiled binaries.

      Package     — Publish + zip only against the already-compiled binaries.
                    Produces distributable artefacts under ./output/.

      Full        — Build + Test + DomainTest + IntegrationTest + SystemTest_Smoke + SystemTest_Simulated + SystemTest_Live +
                     Package in sequence.
                    Use for Preview (push to main) and Production releases.

      Stats       — Read existing .trx files in TestResults/ and output the
                    test summary and 5 slowest tests per category. No build or test run.

      Start       — Install (build + unit test + publish + install to versioned
                    folder + update 'current' junction), then launches the
                    Aspire AppHost (ControlPlane + MigrationAgent) so you can
                    run 'devopsmigrationdev' against it. Ctrl-C to stop.

    Install     — Build + full test pass + publish for the current
              platform, then installs to
                    %USERPROFILE%\source\Tools\MigrationPlatform\{version}\
                    and updates a 'current' junction so that the
                    'devopsmigrationdev' alias always runs the latest build.
                    Shim is written to %USERPROFILE%\.dotnet\tools\ which is
                    already on PATH for any machine with the .NET SDK.

    Workflow matrix:
      PR                   :  Build  →  CodeTest  →  SystemTest_Smoke  →  SystemTest_Simulated  →  SystemTest_Live   (separate steps)
      Preview (main push)  :  Build  →  CodeTest  →  SystemTest_Smoke  →  SystemTest_Simulated  →  SystemTest_Live  →  Package
      Production (release) :  Build  →  CodeTest  →  SystemTest_Smoke  →  SystemTest_Simulated  →  SystemTest_Live  →  Package
      Developer local      :  Install  (or Start to also launch Aspire)

    The -Fast switch skips all system tests (SystemTest, SystemTest_Smoke, SystemTest_Simulated,
    SystemTest_Live) in Install and Start modes for faster iteration.

    Prerequisites:
      - .NET SDK (see global.json)
      - GitVersion.Tool 6.1.0:
            dotnet tool install --global GitVersion.Tool --version 6.1.0
        OR restore from the local tool manifest:
            dotnet tool restore

    Artefact outputs (placed under ./.output/, one zip per RID):
      - MigrationTools-{SemVer}-{rid}.zip

    Each zip contains:
      /                  — devopsMigration CLI (root)
      /ControlPlane/     — Control Plane host
      /MigrationAgent/   — Migration Agent worker
      /TfsMigrationAgent/  — TFS polling agent (win-x64 only)

    RIDs produced: win-x64, win-arm64, linux-x64, osx-x64, osx-arm64

.PARAMETER Mode
    Build | Test | SystemTest | SystemTest_Smoke | SystemTest_Simulated | SystemTest_Live | Package | Full | Stats | Start | Install | RunTest
    If omitted, usage help is printed and the script exits.

.PARAMETER Version
    Override the version string instead of resolving via GitVersion.
    When set, SemVer / AssemblySemVer / InformationalVersion are all
    set to this value and GitVersion is not invoked.

.EXAMPLE
    pwsh ./build.ps1                  # Show available modes and usage
    pwsh ./build.ps1 -Mode Build
    pwsh ./build.ps1 -Mode Test
    pwsh ./build.ps1 -Mode SystemTest
    pwsh ./build.ps1 -Mode SystemTest_Smoke
    pwsh ./build.ps1 -Mode SystemTest_Simulated
    pwsh ./build.ps1 -Mode SystemTest_Live
    pwsh ./build.ps1 -Mode Package
    pwsh ./build.ps1 -Mode Full
    pwsh ./build.ps1 -Mode Start
    pwsh ./build.ps1 -Mode Install
    pwsh ./build.ps1 -Mode Install -Fast  # Skip system tests
    pwsh ./build.ps1 -Mode Stats           # Show stats from last test run
    pwsh ./build.ps1 RunTest "MyTestName"  # Run a single test by (partial) name
    pwsh ./build.ps1 -Version 16.9.3  # Override version
#>
param(
    [Parameter(Position = 0)]
    [ValidateSet('Build', 'Test', 'DomainTest', 'IntegrationTest', 'CodeTest', 'SystemTest', 'SystemTest_Smoke', 'SystemTest_Simulated', 'SystemTest_Live', 'Package', 'Full', 'Stats', 'Start', 'Install', 'RunTest')]
    [string]$Mode = '',

    [string]$Version,

    # Used with -Mode RunTest: runs a single test by (partial) name.
    # Example: .\build.ps1 RunTest "DependencyCommand_SystemTest_AdoSingleProject_ExecutesSuccessfully"
    [Parameter(Position = 1)]
    [string]$TestName,

    [switch]$Fast
)

# Guard against GNU-style flags (for example: --Fast) being consumed as
# positional values (most commonly Version) instead of real PowerShell switches.
if ($PSBoundParameters.ContainsKey('Version') -and $Version -match '^--?[A-Za-z]') {
    Write-Error "Unsupported argument '$Version'. Use PowerShell parameter syntax with a single dash (for example: -Fast)."
    exit 1
}

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

if (-not $Mode) {
    Write-Host ""
    Write-Host "  Azure DevOps Migration Platform — build.ps1" -ForegroundColor Cyan
    Write-Host ""
    Write-Host "  Usage: pwsh ./build.ps1 <Mode> [-Version <ver>] [-Fast]" -ForegroundColor White
    Write-Host ""
    Write-Host "  Modes:" -ForegroundColor Yellow
    Write-Host "    Build              Compile the solution only"
    Write-Host "    Test               Run unit tests only (TestCategory=UnitTests)"
    Write-Host "    DomainTest         Run domain tests only (TestCategory=DomainTests)"
    Write-Host "    IntegrationTest    Run integration tests only (TestCategory=IntegrationTests)"
    Write-Host "    CodeTest           Run all in-process tests (TestCategory=CodeTest): Unit + Domain + Integration"
    Write-Host "    SystemTest         Run all system tests (Smoke + Simulated + Live + Remaining)"
    Write-Host "    SystemTest_Smoke   Run smoke/startup system tests only"
    Write-Host "    SystemTest_Simulated  Run simulated connector system tests only"
    Write-Host "    SystemTest_Live    Run live Azure DevOps system tests only"
    Write-Host "    Package            Build + publish + zip artefacts"
    Write-Host "    Full               Build + all tests + publish + package (all platforms)"
    Write-Host "    Start              Build + tests + install + launch Aspire AppHost"
    Write-Host "    Install            Build + all tests + package + install locally"
    Write-Host "    Stats              Print summary from last test run (.trx files)"
    Write-Host "    RunTest <name>     Run a single test by partial name"
    Write-Host ""
    Write-Host "  Flags:" -ForegroundColor Yellow
    Write-Host "    -Fast              Skip system tests (valid with Start, Install)"
    Write-Host "    -Version <ver>     Override version (e.g. 16.9.3); skips GitVersion"
    Write-Host ""
    Write-Host "  Examples:" -ForegroundColor Yellow
    Write-Host "    pwsh ./build.ps1 Build"
    Write-Host "    pwsh ./build.ps1 Full"
    Write-Host "    pwsh ./build.ps1 Start -Fast"
    Write-Host "    pwsh ./build.ps1 RunTest `"MyTestName`""
    Write-Host ""
    exit 0
}

$RepoRoot = $PSScriptRoot
$SolutionFile = Join-Path $RepoRoot 'DevOpsMigrationPlatform.slnx'
$ArtifactsDir = Join-Path $RepoRoot '.output/build'
$BuildStampFile = Join-Path $ArtifactsDir '.build-stamp'
$TestResultsDir = Join-Path $RepoRoot 'TestResults'
$TimingsFile = Join-Path $TestResultsDir 'build-timings.json'

$AppHostProject = Join-Path $RepoRoot 'src/DevOpsMigrationPlatform.AppHost/DevOpsMigrationPlatform.AppHost.csproj'
$CliMigrationProject = Join-Path $RepoRoot 'src/DevOpsMigrationPlatform.CLI.Migration/DevOpsMigrationPlatform.CLI.Migration.csproj'
$TfsAgentProject = Join-Path $RepoRoot 'src/DevOpsMigrationPlatform.TfsMigrationAgent/DevOpsMigrationPlatform.TfsMigrationAgent.csproj'
$ControlPlaneProject = Join-Path $RepoRoot 'src/DevOpsMigrationPlatform.ControlPlaneHost/DevOpsMigrationPlatform.ControlPlaneHost.csproj'
$AgentProject = Join-Path $RepoRoot 'src/DevOpsMigrationPlatform.MigrationAgent/DevOpsMigrationPlatform.MigrationAgent.csproj'

# Runtime identifiers for per-platform publishing.
# Only win-x64 gets the TfsMigrationAgent/ subfolder; net481 is Windows-only.
$AllRids = @('win-x64', 'win-arm64', 'linux-x64', 'osx-x64', 'osx-arm64')

# ─────────────────────────────────────────────────────────────────────────────
# Helpers
# ─────────────────────────────────────────────────────────────────────────────
$script:StepTimings = [System.Collections.Generic.List[PSCustomObject]]::new()
$script:BuildStart = [System.Diagnostics.Stopwatch]::StartNew()

function Write-Banner {
    param([string]$SemVer, [string]$Mode)
    $width = 60
    $line = '═' * $width
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

function Get-TrxRows {
    # Parse a list of .trx file paths and return per-assembly result rows.
    param([string[]]$TrxPaths)
    $rows = [System.Collections.Generic.List[PSCustomObject]]::new()
    foreach ($path in $TrxPaths) {
        [xml]$xml = Get-Content -LiteralPath $path -Raw
        $ns = @{ t = 'http://microsoft.com/schemas/VisualStudio/TeamTest/2010' }
        $counters = Select-Xml -Xml $xml -XPath '//t:ResultSummary/t:Counters' -Namespace $ns |
            Select-Object -ExpandProperty Node
        if (-not $counters) { continue }
        $passed  = [int]$counters.passed
        $failed  = [int]$counters.failed
        $skipped = [int]$counters.notExecuted
        $total   = [int]$counters.total
        if ($total -eq 0) { continue }
        $assembly = [System.IO.Path]::GetFileNameWithoutExtension($path)
        $defNode = Select-Xml -Xml $xml -XPath '//t:TestDefinitions/t:UnitTest[1]/t:TestMethod' -Namespace $ns |
            Select-Object -ExpandProperty Node
        if ($defNode -and $defNode.className) {
            $assembly = ($defNode.className -split ',')[0] -replace '\.Tests\..*|\.Test\..*', '.Tests'
        }
        $rows.Add([PSCustomObject]@{ Assembly = $assembly; Passed = $passed; Failed = $failed; Skipped = $skipped; Total = $total })
    }
    return $rows
}

function Write-TrxSection {
    # Render a labelled section of per-assembly rows and return totals.
    param([string]$Label, [System.Collections.Generic.List[PSCustomObject]]$Rows)
    if (-not $Rows -or $Rows.Count -eq 0) { return [PSCustomObject]@{ Passed = 0; Failed = 0; Skipped = 0; Total = 0 } }

    $secPassed = 0; $secFailed = 0; $secSkipped = 0; $secTotal = 0

    Write-Host ''
    Write-Host "  $Label" -ForegroundColor DarkCyan
    foreach ($row in $Rows) {
        $secPassed  += $row.Passed
        $secFailed  += $row.Failed
        $secSkipped += $row.Skipped
        $secTotal   += $row.Total
        $icon  = if ($row.Failed -gt 0) { '!' } else { ' ' }
        $color = if ($row.Failed -gt 0) { 'Red' } else { 'Green' }
        $asm   = if ($row.Assembly.Length -gt 42) { $row.Assembly.Substring(0, 39) + '...' } else { $row.Assembly }
        Write-Host ('  {0} {1,-42}  {2,6}  {3,6}  {4,5}  {5,6}' -f $icon, $asm, $row.Passed, $row.Failed, $row.Skipped, $row.Total) -ForegroundColor $color
    }
    Write-Host ('    {0}  {1}  {2}  {3}  {4}' -f ('─' * 42), ('─' * 6), ('─' * 6), ('─' * 5), ('─' * 6)) -ForegroundColor DarkGray
    $subColor = if ($secFailed -gt 0) { 'Red' } else { 'White' }
    Write-Host ('    {0,-42}  {1,6}  {2,6}  {3,5}  {4,6}' -f 'Subtotal', $secPassed, $secFailed, $secSkipped, $secTotal) -ForegroundColor $subColor

    return [PSCustomObject]@{ Passed = $secPassed; Failed = $secFailed; Skipped = $secSkipped; Total = $secTotal }
}

function Write-TestSummary {
    # Parse .trx files from TestResults/ subdirectories (unit/simulated/live/system)
    # and show a categorised test summary.  Falls back to the flat root view for
    # backwards-compat when no subdirectory files exist (e.g. Stats on an old run).

    $allTrxFiles = Get-ChildItem -LiteralPath $TestResultsDir -Filter '*.trx' -Recurse -ErrorAction SilentlyContinue
    if (-not $allTrxFiles -or $allTrxFiles.Count -eq 0) { return }

    $hr = '─' * 78
    Write-Host ''
    Write-Host $hr -ForegroundColor DarkGray
    Write-Host '  Test Summary' -ForegroundColor White
    Write-Host $hr -ForegroundColor DarkGray

    # Column header
    Write-Host ('    {0,-42}  {1,6}  {2,6}  {3,5}  {4,6}' -f 'Assembly', 'Passed', 'Failed', 'Skip', 'Total') -ForegroundColor DarkGray
    Write-Host ('    {0}  {1}  {2}  {3}  {4}' -f ('─' * 42), ('─' * 6), ('─' * 6), ('─' * 5), ('─' * 6)) -ForegroundColor DarkGray

    $totalPassed = 0; $totalFailed = 0; $totalSkipped = 0; $totalTests = 0

    # ── Categorised sections ─────────────────────────────────────────────────
    $categorySections = @(
        [PSCustomObject]@{ Label = 'Unit Tests';               Dir = Join-Path $TestResultsDir 'unit'        },
        [PSCustomObject]@{ Label = 'Domain Tests';             Dir = Join-Path $TestResultsDir 'domain'      },
        [PSCustomObject]@{ Label = 'Integration Tests';        Dir = Join-Path $TestResultsDir 'integration' },
        [PSCustomObject]@{ Label = 'System Tests (Smoke)';     Dir = Join-Path $TestResultsDir 'smoke'       },
        [PSCustomObject]@{ Label = 'System Tests (Simulated)'; Dir = Join-Path $TestResultsDir 'simulated'   },
        [PSCustomObject]@{ Label = 'System Tests (Live)';      Dir = Join-Path $TestResultsDir 'live'        },
        [PSCustomObject]@{ Label = 'System Tests';             Dir = Join-Path $TestResultsDir 'system'      }
    )

    $hasSubdirData = $false
    foreach ($section in $categorySections) {
        if (-not (Test-Path $section.Dir -ErrorAction SilentlyContinue)) { continue }
        $sectionTrx = Get-ChildItem -LiteralPath $section.Dir -Filter '*.trx' -ErrorAction SilentlyContinue
        if (-not $sectionTrx -or $sectionTrx.Count -eq 0) { continue }
        $hasSubdirData = $true
        $rows = Get-TrxRows -TrxPaths ($sectionTrx | Select-Object -ExpandProperty FullName)
        $totals = Write-TrxSection -Label $section.Label -Rows $rows
        $totalPassed  += $totals.Passed
        $totalFailed  += $totals.Failed
        $totalSkipped += $totals.Skipped
        $totalTests   += $totals.Total
    }

    # ── Fallback: flat root .trx files (backwards-compat / legacy Stats run) ─
    if (-not $hasSubdirData) {
        $rootTrx = Get-ChildItem -LiteralPath $TestResultsDir -Filter '*.trx' -ErrorAction SilentlyContinue
        if ($rootTrx -and $rootTrx.Count -gt 0) {
            $rows = Get-TrxRows -TrxPaths ($rootTrx | Select-Object -ExpandProperty FullName)
            Write-Host ''
            foreach ($row in $rows) {
                $totalPassed  += $row.Passed
                $totalFailed  += $row.Failed
                $totalSkipped += $row.Skipped
                $totalTests   += $row.Total
                $icon  = if ($row.Failed -gt 0) { '!' } else { ' ' }
                $color = if ($row.Failed -gt 0) { 'Red' } else { 'Green' }
                $asm   = if ($row.Assembly.Length -gt 42) { $row.Assembly.Substring(0, 39) + '...' } else { $row.Assembly }
                Write-Host ('  {0} {1,-42}  {2,6}  {3,6}  {4,5}  {5,6}' -f $icon, $asm, $row.Passed, $row.Failed, $row.Skipped, $row.Total) -ForegroundColor $color
            }
        }
    }

    Write-Host ''
    Write-Host $hr -ForegroundColor DarkGray
    $summaryIcon  = if ($totalFailed -gt 0) { '!' } else { ' ' }
    $summaryColor = if ($totalFailed -gt 0) { 'Red' } else { 'Green' }
    Write-Host ('  {0} {1,-42}  {2,6}  {3,6}  {4,5}  {5,6}' -f $summaryIcon, 'ALL TESTS', $totalPassed, $totalFailed, $totalSkipped, $totalTests) -ForegroundColor $summaryColor
    Write-Host $hr -ForegroundColor DarkGray

    # ── 5 slowest tests per category ─────────────────────────────────────────
    $slowestSections = @(
        [PSCustomObject]@{ Label = 'Unit Tests';               Dir = Join-Path $TestResultsDir 'unit'        },
        [PSCustomObject]@{ Label = 'Domain Tests';             Dir = Join-Path $TestResultsDir 'domain'      },
        [PSCustomObject]@{ Label = 'Integration Tests';        Dir = Join-Path $TestResultsDir 'integration' },
        [PSCustomObject]@{ Label = 'System Tests (Smoke)';     Dir = Join-Path $TestResultsDir 'smoke'       },
        [PSCustomObject]@{ Label = 'System Tests (Simulated)'; Dir = Join-Path $TestResultsDir 'simulated'   },
        [PSCustomObject]@{ Label = 'System Tests (Live)';      Dir = Join-Path $TestResultsDir 'live'        },
        [PSCustomObject]@{ Label = 'System Tests';             Dir = Join-Path $TestResultsDir 'system'      }
    )

    $anySlowest = $false
    foreach ($sec in $slowestSections) {
        if (-not (Test-Path $sec.Dir -ErrorAction SilentlyContinue)) { continue }
        $secTrx = Get-ChildItem -LiteralPath $sec.Dir -Filter '*.trx' -ErrorAction SilentlyContinue
        if (-not $secTrx -or $secTrx.Count -eq 0) { continue }

        $secResults = foreach ($trx in $secTrx) {
            [xml]$xml = Get-Content -LiteralPath $trx.FullName -Raw
            $ns = @{ t = 'http://microsoft.com/schemas/VisualStudio/TeamTest/2010' }
            Select-Xml -Xml $xml -XPath '//t:Results/t:UnitTestResult' -Namespace $ns |
                Select-Object -ExpandProperty Node |
                Where-Object { $_.duration } |
                ForEach-Object {
                    [PSCustomObject]@{ Name = $_.testName; Duration = [TimeSpan]::Parse($_.duration) }
                }
        }

        $slowest = $secResults | Sort-Object Duration -Descending | Select-Object -First 5
        if (-not $slowest) { continue }

        if (-not $anySlowest) {
            Write-Host ''
            Write-Host ('  {0,3}  {1,-62}  {2,9}' -f '#', 'Test', 'Duration') -ForegroundColor DarkGray
            $anySlowest = $true
        }

        Write-Host ''
        Write-Host "  5 Slowest — $($sec.Label)" -ForegroundColor White
        Write-Host $hr -ForegroundColor DarkGray
        $rank = 1
        foreach ($t in $slowest) {
            $dur = $t.Duration
            $formatted = '{0}:{1:D2}.{2:D3}' -f [int]$dur.TotalMinutes, $dur.Seconds, $dur.Milliseconds
            $shortName = if ($t.Name.Length -gt 62) { $t.Name.Substring(0, 59) + '...' } else { $t.Name }
            Write-Host ('  {0,3}  {1,-62}  {2,9}' -f $rank, $shortName, $formatted) -ForegroundColor DarkYellow
            $rank++
        }
        Write-Host $hr -ForegroundColor DarkGray
    }

    # Fallback: global slowest when no categorised subdirs (legacy Stats run)
    if (-not $anySlowest) {
        $allResults = foreach ($trx in $allTrxFiles) {
            [xml]$xml = Get-Content -LiteralPath $trx.FullName -Raw
            $ns = @{ t = 'http://microsoft.com/schemas/VisualStudio/TeamTest/2010' }
            Select-Xml -Xml $xml -XPath '//t:Results/t:UnitTestResult' -Namespace $ns |
                Select-Object -ExpandProperty Node |
                Where-Object { $_.duration } |
                ForEach-Object {
                    [PSCustomObject]@{ Name = $_.testName; Duration = [TimeSpan]::Parse($_.duration) }
                }
        }
        $slowest = $allResults | Sort-Object Duration -Descending | Select-Object -First 5
        if ($slowest) {
            Write-Host ''
            Write-Host '  5 Slowest Tests' -ForegroundColor White
            Write-Host $hr -ForegroundColor DarkGray
            Write-Host ('  {0,3}  {1,-62}  {2,9}' -f '#', 'Test', 'Duration') -ForegroundColor DarkGray
            Write-Host ('  {0}  {1}  {2}' -f ('─' * 3), ('─' * 62), ('─' * 9)) -ForegroundColor DarkGray
            $rank = 1
            foreach ($t in $slowest) {
                $dur = $t.Duration
                $formatted = '{0}:{1:D2}.{2:D3}' -f [int]$dur.TotalMinutes, $dur.Seconds, $dur.Milliseconds
                $shortName = if ($t.Name.Length -gt 62) { $t.Name.Substring(0, 59) + '...' } else { $t.Name }
                Write-Host ('  {0,3}  {1,-62}  {2,9}' -f $rank, $shortName, $formatted) -ForegroundColor DarkYellow
                $rank++
            }
            Write-Host $hr -ForegroundColor DarkGray
        }
    }

    Write-Host ''
}

function Write-BuildSummary {
    $script:BuildStart.Stop()
    $total = $script:BuildStart.Elapsed

    # Show test counts first (if any tests were run)
    Write-TestSummary

    $hr = '─' * 78
    Write-Host ""
    Write-Host $hr -ForegroundColor DarkGray
    Write-Host '  Build Summary' -ForegroundColor White
    Write-Host $hr -ForegroundColor DarkGray

    # Column header
    Write-Host ('  {0,-54}  {1,9}  {2,7}' -f 'Step', 'Elapsed', 'Tests') -ForegroundColor DarkGray
    Write-Host ('  {0}  {1}  {2}' -f ('─' * 54), ('─' * 9), ('─' * 7)) -ForegroundColor DarkGray

    foreach ($entry in $script:StepTimings) {
        $elapsed = Format-Elapsed $entry.Elapsed.TotalSeconds
        $subdirName = $script:StepTrxDirMap[$entry.Step]
        $testCount = $null
        $testStr = ''
        $testColor = 'Gray'
        if ($subdirName) {
            $testCount = Get-TrxTotalCount (Join-Path $TestResultsDir $subdirName)
            if ($null -ne $testCount) {
                $testStr  = '{0,7}' -f $testCount
                $testColor = if ($testCount -eq 0) { 'DarkYellow' } else { 'Gray' }
            }
        }
        $stepName = if ($entry.Step.Length -gt 54) { $entry.Step.Substring(0, 51) + '...' } else { $entry.Step }
        Write-Host ('  {0,-54}  {1,9}' -f $stepName, $elapsed) -ForegroundColor Gray -NoNewline
        if ($testStr) { Write-Host "  $testStr" -ForegroundColor $testColor } else { Write-Host '' }
        $entry | Add-Member -NotePropertyName 'TestCount' -NotePropertyValue $testCount -Force
    }

    Write-Host ('  {0}  {1}  {2}' -f ('─' * 54), ('─' * 9), ('─' * 7)) -ForegroundColor DarkGray
    Write-Host ('  {0,-54}  {1,9}' -f 'TOTAL', (Format-Elapsed $total.TotalSeconds)) -ForegroundColor White
    Write-Host $hr -ForegroundColor DarkGray
    Write-Host ''

    # Persist timings so 'Stats' mode can display them without a full run
    try {
        New-Item -ItemType Directory -Path $TestResultsDir -Force | Out-Null
        $payload = [PSCustomObject]@{
            RunAt        = (Get-Date -Format 'yyyy-MM-dd HH:mm:ss')
            Mode         = $Mode
            TotalSeconds = $total.TotalSeconds
            Steps        = @($script:StepTimings | ForEach-Object {
                    [PSCustomObject]@{ Step = $_.Step; ElapsedSeconds = $_.Elapsed.TotalSeconds; TestCount = $_.TestCount }
                })
        }
        $payload | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $TimingsFile -Encoding UTF8
    }
    catch { <# non-fatal #> }
}

function Format-Elapsed {
    param([double]$TotalSeconds)
    $ts = [TimeSpan]::FromSeconds($TotalSeconds)
    '{0}:{1:D2}.{2:D3}' -f [int]$ts.TotalMinutes, $ts.Seconds, $ts.Milliseconds
}

function Get-TrxTotalCount {
    # Sum the 'total' counter across all .trx files in a directory.
    param([string]$Dir)
    if (-not (Test-Path $Dir -ErrorAction SilentlyContinue)) { return $null }
    $trxFiles = Get-ChildItem -LiteralPath $Dir -Filter '*.trx' -ErrorAction SilentlyContinue
    if (-not $trxFiles -or $trxFiles.Count -eq 0) { return $null }
    $total = 0
    foreach ($f in $trxFiles) {
        [xml]$xml = Get-Content -LiteralPath $f.FullName -Raw
        $c = $xml.TestRun.ResultSummary.Counters
        if ($c) { $total += [int]$c.total }
    }
    return $total
}

# Maps build step descriptions to the TRX subdirectory that holds their results.
$script:StepTrxDirMap = @{
    'Running unit tests (TestCategory=UnitTests)'                                    = 'unit'
    'Running domain tests (TestCategory=DomainTests)'                                = 'domain'
    'Running integration tests (TestCategory=IntegrationTests)'                      = 'integration'
    'Running smoke system tests (TestCategory=SystemTest_Smoke)'                     = 'smoke'
    'Running simulated system tests (TestCategory=SystemTest_Simulated)'             = 'simulated'
    'Running live system tests (TestCategory=SystemTest_Live)'                       = 'live'
    'Running remaining system tests (SystemTest excluding Smoke/Simulated/Live)'     = 'system'
}

function Write-BuildTimings {
    if (-not (Test-Path $TimingsFile)) {
        Write-Host '  (No saved build timings found — run a build/test first)' -ForegroundColor DarkGray
        return
    }
    $data = Get-Content -LiteralPath $TimingsFile -Raw | ConvertFrom-Json
    $hr = '─' * 78
    Write-Host ''
    Write-Host $hr -ForegroundColor DarkGray
    Write-Host ('  Build Timings  (last run: {0}  mode: {1})' -f $data.RunAt, $data.Mode) -ForegroundColor White
    Write-Host $hr -ForegroundColor DarkGray

    # Column header
    Write-Host ('  {0,-54}  {1,9}  {2,7}' -f 'Step', 'Elapsed', 'Tests') -ForegroundColor DarkGray
    Write-Host ('  {0}  {1}  {2}' -f ('─' * 54), ('─' * 9), ('─' * 7)) -ForegroundColor DarkGray

    foreach ($entry in $data.Steps) {
        $elapsed  = Format-Elapsed $entry.ElapsedSeconds
        $stepName = if ($entry.Step.Length -gt 54) { $entry.Step.Substring(0, 51) + '...' } else { $entry.Step }
        $testStr  = ''
        $testColor = 'Gray'
        if ($null -ne $entry.TestCount) {
            $testStr   = '{0,7}' -f $entry.TestCount
            $testColor = if ($entry.TestCount -eq 0) { 'DarkYellow' } else { 'Gray' }
        }
        Write-Host ('  {0,-54}  {1,9}' -f $stepName, $elapsed) -ForegroundColor Gray -NoNewline
        if ($testStr) { Write-Host "  $testStr" -ForegroundColor $testColor } else { Write-Host '' }
    }

    Write-Host ('  {0}  {1}  {2}' -f ('─' * 54), ('─' * 9), ('─' * 7)) -ForegroundColor DarkGray
    Write-Host ('  {0,-54}  {1,9}' -f 'TOTAL', (Format-Elapsed $data.TotalSeconds)) -ForegroundColor White
    Write-Host $hr -ForegroundColor DarkGray
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
    }
    else {
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
            $depth += [regex]::Matches($line, '\{').Count
            $depth -= [regex]::Matches($line, '\}').Count
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
    }
    catch {
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
    # Write a stamp so subsequent modes in the same or a later invocation can skip rebuilding.
    New-Item -ItemType Directory -Path $ArtifactsDir -Force | Out-Null
    [System.IO.File]::WriteAllText($BuildStampFile, (Get-Date -Format 'o'))
}

function Invoke-EnsureBuilt {
    param($VersionArgs)
    if (Test-Path $BuildStampFile) {
        $stampAge = (Get-Date) - (Get-Item $BuildStampFile).LastWriteTime
        $sourceChanged = @(
            Get-ChildItem -Path (Join-Path $RepoRoot 'src')   -Recurse -Include '*.cs', '*.csproj', '*.slnx', 'Directory.Build.*', 'Directory.Packages.*' -ErrorAction SilentlyContinue
            Get-ChildItem -Path (Join-Path $RepoRoot 'tests') -Recurse -Include '*.cs', '*.csproj', '*.slnx', 'Directory.Build.*', 'Directory.Packages.*' -ErrorAction SilentlyContinue
        ) | Where-Object { $_.LastWriteTime -gt (Get-Item $BuildStampFile).LastWriteTime }
        if (-not $sourceChanged) {
            Write-Host "`n==> Build already up-to-date (stamp: $([System.IO.File]::ReadAllText($BuildStampFile))). Skipping build." -ForegroundColor DarkGray
            return
        }
        Write-Host "`n==> Source changes detected since last build. Rebuilding..." -ForegroundColor Yellow
    }
    Invoke-Build -VersionArgs $VersionArgs
}

function Invoke-UnitTests {
    # Clear all stale .trx files (including subdirs) so the summary only reflects the current run.
    if (Test-Path $TestResultsDir) {
        Get-ChildItem -LiteralPath $TestResultsDir -Filter '*.trx' -Recurse -ErrorAction SilentlyContinue |
            Remove-Item -Force -ErrorAction SilentlyContinue
    }
    $unitDir = Join-Path $TestResultsDir 'unit'
    New-Item -ItemType Directory -Path $unitDir -Force | Out-Null
    Invoke-Step 'Running unit tests (TestCategory=UnitTests)' {
        dotnet test $SolutionFile `
            --no-build `
            --configuration Release `
            --filter 'TestCategory=UnitTests' `
            --logger 'trx' `
            --logger 'console;verbosity=normal' `
            --results-directory $unitDir
    }
}

function Invoke-DomainTests {
    $domainDir = Join-Path $TestResultsDir 'domain'
    New-Item -ItemType Directory -Path $domainDir -Force | Out-Null
    Invoke-Step 'Running domain tests (TestCategory=DomainTests)' {
        dotnet test $SolutionFile `
            --no-build `
            --configuration Release `
            --filter 'TestCategory=DomainTests' `
            --logger 'trx' `
            --logger 'console;verbosity=normal' `
            --results-directory $domainDir
    }
}

function Invoke-AllTests {
    # Run all categories in order so the summary is broken out by section.
    Invoke-UnitTests
    Invoke-DomainTests
    Invoke-IntegrationTests
    Invoke-SmokeSystemTests
    Invoke-SimulatedSystemTests
    Invoke-LiveSystemTests
    Invoke-RemainingSystemTests
}

function Invoke-IntegrationTests {
    $integrationDir = Join-Path $TestResultsDir 'integration'
    New-Item -ItemType Directory -Path $integrationDir -Force | Out-Null
    Invoke-Step 'Running integration tests (TestCategory=IntegrationTests)' {
        dotnet test $SolutionFile `
            --no-build `
            --configuration Release `
            --filter 'TestCategory=IntegrationTests' `
            --logger 'trx' `
            --logger 'console;verbosity=normal' `
            --results-directory $integrationDir
    }
}

function Invoke-SmokeSystemTests {
    $smokeDir = Join-Path $TestResultsDir 'smoke'
    New-Item -ItemType Directory -Path $smokeDir -Force | Out-Null
    # Only tests tagged [TestCategory("SystemTest_Smoke")]
    Invoke-Step 'Running smoke system tests (TestCategory=SystemTest_Smoke)' {
        dotnet test $SolutionFile `
            --no-build `
            --configuration Release `
            --filter 'TestCategory=SystemTest_Smoke' `
            --logger 'trx' `
            --logger 'console;verbosity=normal' `
            --results-directory $smokeDir
    }
}

function Invoke-SimulatedSystemTests {
    $simulatedDir = Join-Path $TestResultsDir 'simulated'
    New-Item -ItemType Directory -Path $simulatedDir -Force | Out-Null
    # Only tests tagged [TestCategory("SystemTest_Simulated")]
    Invoke-Step 'Running simulated system tests (TestCategory=SystemTest_Simulated)' {
        dotnet test $SolutionFile `
            --no-build `
            --configuration Release `
            --filter 'TestCategory=SystemTest_Simulated' `
            --logger 'trx' `
            --logger 'console;verbosity=normal' `
            --results-directory $simulatedDir
    }
}

function Invoke-LiveSystemTests {
    $liveDir = Join-Path $TestResultsDir 'live'
    New-Item -ItemType Directory -Path $liveDir -Force | Out-Null
    # Only tests tagged [TestCategory("SystemTest_Live")]
    Invoke-Step 'Running live system tests (TestCategory=SystemTest_Live)' {
        dotnet test $SolutionFile `
            --no-build `
            --configuration Release `
            --filter 'TestCategory=SystemTest_Live' `
            --logger 'trx' `
            --logger 'console;verbosity=normal' `
            --results-directory $liveDir
    }
}

function Invoke-RemainingSystemTests {
    $systemDir = Join-Path $TestResultsDir 'system'
    New-Item -ItemType Directory -Path $systemDir -Force | Out-Null
    # System tests that are tagged SystemTest but NOT in the smoke/simulated/live sub-categories.
    Invoke-Step 'Running remaining system tests (SystemTest excluding Smoke/Simulated/Live)' {
        dotnet test $SolutionFile `
            --no-build `
            --configuration Release `
            --filter 'TestCategory=SystemTest&TestCategory!=SystemTest_Smoke&TestCategory!=SystemTest_Simulated&TestCategory!=SystemTest_Live' `
            --logger 'trx' `
            --logger 'console;verbosity=normal' `
            --results-directory $systemDir
    }
}

function Invoke-Publish {
    param($StagingDir, $VersionArgs, [string[]]$TargetRids = $script:AllRids)

    $script:CliMigrationOutByRid = @{}
    $script:ControlPlaneOutByRid = @{}
    $script:AgentOutByRid = @{}

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

    # TfsMigrationAgent — win-x64 only (net481 is Windows-only, no RID flag needed)
    $script:TfsAgentOut = Join-Path $StagingDir 'tfs-agent-win-x64'
    Invoke-Step 'Publishing TfsMigrationAgent [win-x64]' {
        dotnet publish $TfsAgentProject `
            --configuration Release `
            --no-build `
            --output $script:TfsAgentOut `
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

        # ── TfsMigrationAgent subfolder (win-x64 only) ──────────────────────
        # First-class polling agent for TFS; peer of MigrationAgent.
        if ($rid -eq 'win-x64') {
            $tfsAgentSubDir = Join-Path $zipStaging 'TfsMigrationAgent'
            New-Item -ItemType Directory -Path $tfsAgentSubDir -Force | Out-Null
            Copy-Item -Path (Join-Path $script:TfsAgentOut '*') -Destination $tfsAgentSubDir -Recurse -Force
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
    }
    finally {
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
    }
    elseif ($IsMacOS) {
        $arch = [System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture
        if ($arch -eq [System.Runtime.InteropServices.Architecture]::Arm64) { return 'osx-arm64' }
        return 'osx-x64'
    }
    else {
        return 'linux-x64'
    }
}

function Invoke-Install {
    param([string]$SemVer)

    $rid = Get-CurrentRid
    $displayRid = $rid -replace '^osx-', 'macos-'
    $zip = Join-Path $ArtifactsDir "MigrationTools-$SemVer-$displayRid.zip"

    if (-not (Test-Path $zip)) {
        Write-Error "Package not found: $zip`nRun './build.ps1 -Mode Package' (or 'Install') to build the package first."
        exit 1
    }

    # ── Install root: %USERPROFILE%\source\Tools\MigrationPlatform\ ──────────
    $installRoot = Join-Path $env:USERPROFILE 'source\Tools\MigrationPlatform'
    $versionedDir = Join-Path $installRoot $SemVer
    $currentDir = Join-Path $installRoot 'current'

    Write-Host "`n==> Installing $SemVer [$rid] from package to $versionedDir" -ForegroundColor Cyan
    Write-Host "  Source: $zip"

    # Remove any previous install for this version then extract the full package.
    # The zip contains: CLI at root, ControlPlane/, MigrationAgent/, TfsMigrationAgent/ (win-x64).
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
        }
        else {
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
# Stats / RunTest modes — no version resolution, no build required
# ─────────────────────────────────────────────────────────────────────────────
if ($Mode -eq 'Stats') {
    Write-BuildTimings
    Write-TestSummary
    exit 0
}

if ($Mode -eq 'RunTest') {
    if (-not $TestName) {
        Write-Error ('Usage: .\build.ps1 RunTest "<TestName>"' + "`nProvide a full or partial test method name via -TestName.")
        exit 1
    }
    Invoke-Step 'Building solution (incremental)' {
        dotnet build $SolutionFile --configuration Release
    }
    Write-Host "`n==> Running single test: $TestName" -ForegroundColor Cyan
    dotnet test $SolutionFile `
        --no-build `
        --configuration Release `
        --filter "FullyQualifiedName~$TestName" `
        --logger 'trx' `
        --logger 'console;verbosity=normal' `
        --results-directory $TestResultsDir
    exit $LASTEXITCODE
}

# ─────────────────────────────────────────────────────────────────────────────
# Orchestration
# ─────────────────────────────────────────────────────────────────────────────
if ($Version) {
    Write-Host "`n==> Using explicit version override: $Version" -ForegroundColor Cyan
    $SemVer = $Version
    $AssemblySemVer = $Version
    $InformationalVersion = $Version
}
else {
    $versionInfo = Resolve-GitVersion

    $SemVer = $versionInfo.SemVer
    $AssemblySemVer = $versionInfo.AssemblySemVer
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
        # ── Unit tests only ──────────────────────────────────────────────────
        Invoke-EnsureBuilt -VersionArgs $VersionArgs
        Invoke-UnitTests

        Write-Host "`n==> Unit tests complete!" -ForegroundColor Green
        Write-BuildSummary
    }

    'DomainTest' {
        # ── Domain tests only ────────────────────────────────────────────────
        Invoke-EnsureBuilt -VersionArgs $VersionArgs
        if (Test-Path $TestResultsDir) {
            Get-ChildItem -LiteralPath $TestResultsDir -Filter '*.trx' -Recurse -ErrorAction SilentlyContinue |
                Remove-Item -Force -ErrorAction SilentlyContinue
        }
        Invoke-DomainTests

        Write-Host "`n==> Domain tests complete!" -ForegroundColor Green
        Write-BuildSummary
    }

    'IntegrationTest' {
        # ── Integration tests only ───────────────────────────────────────────
        Invoke-EnsureBuilt -VersionArgs $VersionArgs
        if (Test-Path $TestResultsDir) {
            Get-ChildItem -LiteralPath $TestResultsDir -Filter '*.trx' -Recurse -ErrorAction SilentlyContinue |
                Remove-Item -Force -ErrorAction SilentlyContinue
        }
        Invoke-IntegrationTests

        Write-Host "`n==> Integration tests complete!" -ForegroundColor Green
        Write-BuildSummary
    }

    'CodeTest' {
        # ── All in-process tests: Unit + Domain + Integration ────────────────
        Invoke-EnsureBuilt -VersionArgs $VersionArgs
        Invoke-UnitTests  # clears stale trx files
        Invoke-DomainTests
        Invoke-IntegrationTests

        Write-Host "`n==> Code tests complete!" -ForegroundColor Green
        Write-BuildSummary
    }

    'SystemTest' {
        # ── Smoke, simulated, then live system tests ─────────────────────────
        # Clear stale .trx files so the summary only reflects this run.
        Invoke-EnsureBuilt -VersionArgs $VersionArgs
        if (Test-Path $TestResultsDir) {
            Get-ChildItem -LiteralPath $TestResultsDir -Filter '*.trx' -Recurse -ErrorAction SilentlyContinue |
                Remove-Item -Force -ErrorAction SilentlyContinue
        }
        Invoke-SmokeSystemTests
        Invoke-SimulatedSystemTests
        Invoke-LiveSystemTests
        Invoke-RemainingSystemTests

        Write-Host "`n==> System tests complete!" -ForegroundColor Green
        Write-BuildSummary
    }

    'SystemTest_Smoke' {
        # ── Smoke startup system tests only ──────────────────────────────────
        Invoke-EnsureBuilt -VersionArgs $VersionArgs
        if (Test-Path $TestResultsDir) {
            Get-ChildItem -LiteralPath $TestResultsDir -Filter '*.trx' -Recurse -ErrorAction SilentlyContinue |
                Remove-Item -Force -ErrorAction SilentlyContinue
        }
        Invoke-SmokeSystemTests

        Write-Host "`n==> Smoke system tests complete!" -ForegroundColor Green
        Write-BuildSummary
    }

    'SystemTest_Simulated' {
        # ── Simulated system tests only ───────────────────────────────────────
        Invoke-EnsureBuilt -VersionArgs $VersionArgs
        if (Test-Path $TestResultsDir) {
            Get-ChildItem -LiteralPath $TestResultsDir -Filter '*.trx' -Recurse -ErrorAction SilentlyContinue |
                Remove-Item -Force -ErrorAction SilentlyContinue
        }
        Invoke-SimulatedSystemTests

        Write-Host "`n==> Simulated system tests complete!" -ForegroundColor Green
        Write-BuildSummary
    }

    'SystemTest_Live' {
        # ── Live system tests only ─────────────────────────────────────────────
        Invoke-EnsureBuilt -VersionArgs $VersionArgs
        if (Test-Path $TestResultsDir) {
            Get-ChildItem -LiteralPath $TestResultsDir -Filter '*.trx' -Recurse -ErrorAction SilentlyContinue |
                Remove-Item -Force -ErrorAction SilentlyContinue
        }
        Invoke-LiveSystemTests

        Write-Host "`n==> Live system tests complete!" -ForegroundColor Green
        Write-BuildSummary
    }

    'RunTest' {
        # Handled above before version resolution — should not reach here.
        Write-Error "RunTest mode must be handled before version resolution. This is a bug."
        exit 1
    }

    'Package' {
        # ── Publish + zip only ────────────────────────────────────────────────
        Invoke-EnsureBuilt -VersionArgs $VersionArgs
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
        # ── Everything: Build + Test + DomainTest + SystemTest_Smoke + SystemTest_Simulated + SystemTest_Live + Package ──
        Invoke-Build       -VersionArgs $VersionArgs
        Invoke-UnitTests
        Invoke-DomainTests
        Invoke-IntegrationTests
        Invoke-SmokeSystemTests
        Invoke-SimulatedSystemTests
        Invoke-LiveSystemTests
        Invoke-RemainingSystemTests
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
        Invoke-DomainTests
        Invoke-IntegrationTests
        if (-not $Fast) {
            Invoke-SmokeSystemTests
            Invoke-SimulatedSystemTests
            Invoke-LiveSystemTests
            Invoke-RemainingSystemTests
        }
        else {
            Write-Host "`n==> Skipping system tests (-Fast)" -ForegroundColor Yellow
        }
        Invoke-Publish -StagingDir $StagingDir -VersionArgs $VersionArgs -TargetRids @($localRid)
        Invoke-Package -SemVer $SemVer -StagingDir $StagingDir -TargetRids @($localRid)
        $installedDir = Invoke-Install -SemVer $SemVer
        Write-BuildSummary
        Start-AppHost -InstallPath $installedDir
    }

    'Stats' {
        # ── Read existing .trx files and print test summary + slowest tests ──
        Write-TestSummary
    }

    'Install' {
        # ── Build + all tests + package (this platform only) + extract package to install dir ─
        # The zip contains the full layout: CLI at root, ControlPlane/, MigrationAgent/,
        # TfsMigration/ (win-x64 only). Invoke-Install locates the zip and extracts it.
        $localRid = Get-CurrentRid
        Invoke-Build   -VersionArgs $VersionArgs
        if ($Fast) {
            Write-Host "`n==> Running unit, domain, and integration tests only (-Fast)" -ForegroundColor Yellow
            Invoke-UnitTests
            Invoke-DomainTests
            Invoke-IntegrationTests
        }
        else {
            Invoke-AllTests
        }
        Invoke-Publish -StagingDir $StagingDir -VersionArgs $VersionArgs -TargetRids @($localRid)
        Invoke-Package -SemVer $SemVer -StagingDir $StagingDir -TargetRids @($localRid)
        Invoke-Install -SemVer $SemVer
        Write-BuildSummary
    }
}
