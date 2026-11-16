#Requires -Modules Pester
# Martin, 2026-11-16: the end-to-end proof is a real migration on Azure DevOps Services, covering at
# least REST -> REST and Object Model -> REST. A ticked checkbox is not proof; the item names its two
# live scenarios at Ready, and at Done it shows a run of each with the target inspected.

BeforeAll {
    $script:dow = Get-Content (Join-Path $PSScriptRoot '..\..\..\platform\development\definition-of-workflow.json') -Raw | ConvertFrom-Json
    function Test-ShiftMet([string]$name, [string]$text) {
        $s = $dow.shifts | Where-Object name -eq $name
        -not ($s.checks | Where-Object { -not [regex]::IsMatch($text, $_.pattern) })
    }
    $script:readyBody = @"
**Result:** ready
Live E2E scenarios:
- REST -> REST: scenarios/SystemTest-Live-Migrate-AzureDevOps-Complete.json
- Object Model -> REST: scenarios/SystemTest-Live-Migrate-TFS-AzureDevOps-Complete.json
"@
    $script:tickedOnly = @"
PR #300 merged.
- [x] 3. Real end-to-end migration completes correctly (live scenario; target state inspected)
- [x] Migration Tools mapping resolved; README parity table and module matrix updated
Confidence: 92%
"@
    $script:proven = $tickedOnly + @"

Live E2E (REST -> REST): run adpe2e-300-1, scenarios/SystemTest-Live-Migrate-AzureDevOps-Complete.json, passed; target inspected: 12 work items, links and history match
Live E2E (Object Model -> REST): run adpe2e-300-2, scenarios/SystemTest-Live-Migrate-TFS-AzureDevOps-Complete.json, passed; target inspected: 12 work items, links and history match
"@
}

Describe 'Live end-to-end proof in the Definition of Workflow' {
    It 'Ready needs both live scenarios named' {
        Test-ShiftMet 'Ready' '**Result:** ready' | Should -BeFalse
        Test-ShiftMet 'Ready' $readyBody | Should -BeTrue
    }
    It 'Done is not met by ticked boxes alone' {
        Test-ShiftMet 'Done' $tickedOnly | Should -BeFalse
    }
    It 'Done is not met by one path only' {
        $one = $tickedOnly + "`nLive E2E (REST -> REST): run adpe2e-300-1, scenarios/x.json, passed; target inspected: ok"
        Test-ShiftMet 'Done' $one | Should -BeFalse
    }
    It 'Done is met when both runs are evidenced with the target inspected' {
        Test-ShiftMet 'Done' $proven | Should -BeTrue
    }
    It 'ParentDone needs both runs for the whole outcome' {
        $parent = "- [x] All sub-issues are closed`n- [x] A final live end-to-end migration of the whole outcome passes"
        Test-ShiftMet 'ParentDone' $parent | Should -BeFalse
        Test-ShiftMet 'ParentDone' ($parent + "`n" + ($proven -replace '(?s)^.*?(Live E2E)', '$1')) | Should -BeTrue
    }
}
