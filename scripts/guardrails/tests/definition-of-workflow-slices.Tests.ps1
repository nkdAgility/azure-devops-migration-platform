#Requires -Modules Pester
# Martin, 2026-11-16: a delivery slice is only valid when it is a complete vertical slice that can be
# shipped to production and delivers value. BrokenDown needs every listed slice to name its value and
# its own two live end-to-end scenarios.

BeforeAll {
    $script:dow = Get-Content (Join-Path $PSScriptRoot '..\..\..\platform\development\definition-of-workflow.json') -Raw | ConvertFrom-Json
    function Test-BrokenDown([string]$text) {
        $s = $dow.shifts | Where-Object name -eq 'BrokenDown'
        -not ($s.checks | Where-Object { -not [regex]::IsMatch($text, $_.pattern) })
    }
    $script:line = { param($n) "- #${n}: value: an operator migrates area paths; REST -> REST: scenarios/a.json; Object Model -> REST: scenarios/b.json" }
}

Describe 'BrokenDown needs every slice shippable alone' {
    It 'is met when every slice names its value and both scenarios' {
        Test-BrokenDown ("Broken down into #301, #302`n" + (& $line 301) + "`n" + (& $line 302)) | Should -BeTrue
    }
    It 'is not met by a bare list of sub-issues (the #169 proposal)' {
        Test-BrokenDown "Broken down into #301, #302, #303" | Should -BeFalse
    }
    It 'is not met when one slice lacks its scenarios' {
        Test-BrokenDown ("Broken down into #301, #302`n" + (& $line 301) + "`n- #302: value: the Control Plane holds the Import child") | Should -BeFalse
    }
    It 'is not met when one slice names no value' {
        Test-BrokenDown ("Broken down into #301, #302`n" + (& $line 301) + "`n- #302: REST -> REST: scenarios/a.json; Object Model -> REST: scenarios/b.json") | Should -BeFalse
    }
}
