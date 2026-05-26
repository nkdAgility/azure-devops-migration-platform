param(
    [string]$RepoRoot = (Get-Location).Path
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Get-CommandToken {
    param([string]$CommandText)
    if ([string]::IsNullOrWhiteSpace($CommandText)) {
        return ""
    }

    $trimmed = $CommandText.Trim()
    if ($trimmed.StartsWith("/")) {
        $trimmed = $trimmed.Substring(1)
    }

    return ($trimmed -split "\s+")[0]
}

function Resolve-ExtensionsFile {
    param([string]$RootPath)
    return Join-Path $RootPath ".specify\extensions.yml"
}

function Parse-ExtensionsYaml {
    param([string]$ExtensionsFile)

    if (Get-Command ConvertFrom-Yaml -ErrorAction SilentlyContinue) {
        $raw = Get-Content -Path $ExtensionsFile -Raw
        return $raw | ConvertFrom-Yaml
    }

    $python = Get-Command python -ErrorAction SilentlyContinue
    if (-not $python) {
        throw "No YAML parser available. Install PowerShell 7+ (ConvertFrom-Yaml) or Python with PyYAML."
    }

    $probe = & $python.Path -c "import yaml" 2>$null
    if ($LASTEXITCODE -ne 0) {
        throw "Python is available but PyYAML is missing. Install PyYAML to validate .specify/extensions.yml."
    }

    $json = & $python.Path -c "import json,sys,yaml;print(json.dumps(yaml.safe_load(open(sys.argv[1], encoding='utf-8'))))" $ExtensionsFile
    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($json)) {
        throw "Failed to parse .specify/extensions.yml via Python."
    }

    return $json | ConvertFrom-Json -Depth 100
}

function Get-HookItems {
    param(
        [object]$Config,
        [string]$HookName
    )

    $hooksNode = $Config.hooks
    if (-not $hooksNode) {
        return @()
    }

    $hookItems = $hooksNode.$HookName
    if (-not $hookItems) {
        return @()
    }

    if ($hookItems -is [System.Array]) {
        return $hookItems
    }

    return @($hookItems)
}

function Test-LocalCommandOrSkill {
    param(
        [string]$RootPath,
        [string]$CommandToken
    )

    if ([string]::IsNullOrWhiteSpace($CommandToken)) {
        return $false
    }

    $commandPath = Join-Path $RootPath (".agents\commands\{0}.md" -f $CommandToken)
    if (Test-Path -LiteralPath $commandPath) {
        return $true
    }

    $skillPath = Join-Path $RootPath (".agents\skills\{0}\SKILL.md" -f $CommandToken)
    return (Test-Path -LiteralPath $skillPath)
}

$extensionsFile = Resolve-ExtensionsFile -RootPath $RepoRoot
if (-not (Test-Path -LiteralPath $extensionsFile)) {
    throw "Missing required orchestrator file: $extensionsFile"
}

$config = Parse-ExtensionsYaml -ExtensionsFile $extensionsFile

$requiredHookCommands = @(
    @{ Hook = "before_implement"; Command = "speckit.superb.tdd" },
    @{ Hook = "after_implement";  Command = "speckit.superb.verify" },
    @{ Hook = "after_tasks";      Command = "speckit.superb.review" }
)

foreach ($req in $requiredHookCommands) {
    $items = Get-HookItems -Config $config -HookName $req.Hook
    if (-not $items -or $items.Count -eq 0) {
        throw "Missing required hook '$($req.Hook)' in .specify/extensions.yml."
    }

    $match = $items | Where-Object {
        $_.command -and (Get-CommandToken -CommandText $_.command) -eq $req.Command
    } | Select-Object -First 1

    if (-not $match) {
        throw "Hook '$($req.Hook)' is missing required command '$($req.Command)'."
    }

    if ($match.enabled -eq $false) {
        throw "Required command '$($req.Command)' is disabled in hook '$($req.Hook)'."
    }

    if ($match.optional -eq $true) {
        throw "Required command '$($req.Command)' must not be optional in hook '$($req.Hook)'."
    }
}

$mandatoryHookItems = @()
foreach ($hookName in @("before_implement", "after_implement", "after_tasks")) {
    $mandatoryHookItems += Get-HookItems -Config $config -HookName $hookName |
        Where-Object { $_.enabled -ne $false -and $_.optional -eq $false }
}

foreach ($item in $mandatoryHookItems) {
    $token = Get-CommandToken -CommandText ([string]$item.command)
    if (-not (Test-LocalCommandOrSkill -RootPath $RepoRoot -CommandToken $token)) {
        throw "Mandatory hook command '$token' does not resolve to a local command or skill artifact."
    }
}

$requiredArtifacts = @(
    ".agents\commands\nkda-tddsn-autonomous.md",
    ".agents\commands\nkda-core-tasks-architecture-compliance.md",
    ".agents\commands\nkda-core-implementation-architecture-compliance.md",
    ".agents\skills\nkda-core-tasks-architecture-compliance\SKILL.md",
    ".agents\skills\nkda-core-implementation-architecture-compliance\SKILL.md"
)

foreach ($artifact in $requiredArtifacts) {
    $fullPath = Join-Path $RepoRoot $artifact
    if (-not (Test-Path -LiteralPath $fullPath)) {
        throw "Missing required orchestrator artifact: $artifact"
    }
}

Write-Host "Orchestrator guardrails validation passed."
