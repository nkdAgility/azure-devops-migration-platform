Set-Location "c:\Users\MartinHinshelwoodNKD\source\repos\azure-devops-migration-tools2"
$agents = ".github\agents"

function Add-Schema([string]$FilePath, [string]$Schema) {
    $raw = [System.IO.File]::ReadAllText($FilePath)
    $marker = "`n```"
    $idx = $raw.LastIndexOf($marker)
    if ($idx -lt 0) {
        Write-Host "WARN: marker not found in $FilePath"
        return
    }
    $before = $raw.Substring(0, $idx).TrimEnd()
    $after  = $raw.Substring($idx)
    $newContent = $before + "`n`n" + $Schema.Trim() + "`n" + $after
    [System.IO.File]::WriteAllText($FilePath, $newContent, [System.Text.Encoding]::UTF8)
    Write-Host "Updated $FilePath"
}

$specSchema = @"
    ## Output Schema

    Every response from this agent MUST be valid JSON matching this schema. No prose - structured contract only.

    ``````json
    {
        "feature_file": "tests/acceptance/<area>/<feature-name>.feature",
        "feature_name": "string",
        "scenarios": [
        {
            "title": "string",
            "given": ["string"],
            "when": ["string"],
            "then": ["string"]
        }
        ],
        "architectural_flags": ["string"]
    }
    ``````

    - architectural_flags: empty array [] if no issues; one message string per flag raised.
    - feature_file: relative path to the file written to disk.
    "@

$testGenSchema = @"
    ## Output Schema

    Every response from this agent MUST be valid JSON matching this schema. No prose - structured contract only.

    ``````json
    {
        "feature_file": "tests/acceptance/<area>/<feature-name>.feature",
        "steps_file": "tests/<Project>.Tests/<Area>/<FeatureName>Steps.cs",
        "context_file": "tests/<Project>.Tests/<Area>/<FeatureName>Context.cs",
        "step_count": 0,
        "status": "pending",
        "errors": ["string"]
    }
    ``````

    - status: always "pending" - all step bodies throw PendingStepException.
    - step_count: total number of [Given]/[When]/[Then] methods generated.
    - errors: empty array [] if no issues; one message string per error.
    "@

$implSchema = @"
    ## Output Schema

    Every response from this agent MUST be valid JSON matching this schema. No prose - structured contract only.

    ``````json
    {
        "files_changed": ["string"],
        "docs_updated": ["string"],
        "pending_steps_remaining": 0,
        "tests_passing": true,
        "notes": ["string"]
    }
    ``````

    - pending_steps_remaining: must be 0 before handoff to Reviewer Agent.
    - tests_passing: must be true before handoff to Reviewer Agent.
    - notes: empty array [] if no issues; one message string per observation.
    "@

$reviewSchema = @"
    ## Output Schema

    Every response from this agent MUST be valid JSON matching this schema. No prose - structured contract only.

    ``````json
    {
        "verdict": "Approved | Rejected",
        "findings": [
        {
            "file": "string",
            "line": 0,
            "rule": "string",
            "issue": "string"
        }
        ],
        "required_changes": ["string"]
    }
    ``````

    - verdict: "Approved" or "Rejected" only - no other values.
    - findings: empty array [] on approval; at least one entry per rejection reason.
    - required_changes: empty array [] on approval; clear actionable items on rejection.
    "@

Add-Schema "$agents\specification-agent.agent.md" $specSchema
Add-Schema "$agents\test-generator.agent.md" $testGenSchema
Add-Schema "$agents\implementer.agent.md" $implSchema
Add-Schema "$agents\reviewer.agent.md" $reviewSchema

Write-Host "All agent schemas added."
