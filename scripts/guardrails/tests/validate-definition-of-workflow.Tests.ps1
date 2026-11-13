#Requires -Modules Pester
# No shift of the Definition of Workflow may be met by a brand-new Backlog Item: a check that matches
# the issue template's own placeholder text would move unassessed work on (found 2026-11-13 on the
# Ready and ParentDone shifts).

BeforeAll {
    $script:tool = Join-Path $PSScriptRoot '..\validate-definition-of-workflow.ps1'
    $script:repo = Resolve-Path (Join-Path $PSScriptRoot '..\..\..')
    $script:template = Join-Path $repo '.github\ISSUE_TEMPLATE\backlog-item.md'
    function New-Definition([string]$readyPattern) {
        $d = Get-Content (Join-Path $repo 'platform\development\definition-of-workflow.json') -Raw | ConvertFrom-Json
        ($d.shifts | Where-Object name -eq 'Ready').checks[0].pattern = $readyPattern
        $p = Join-Path $TestDrive "dow-$([guid]::NewGuid()).json"
        $d | ConvertTo-Json -Depth 20 | Set-Content $p -Encoding utf8
        $p
    }
}

Describe 'validate-definition-of-workflow.ps1' {
    It 'passes the repository definition' {
        & pwsh -NoProfile -File $tool 2>&1 | Out-Null
        $LASTEXITCODE | Should -Be 0
    }

    It 'fails a shift whose checks match template placeholder text' {
        $bad = New-Definition '(?im)^\*\*Result:\*\*\s*ready'
        $out = & pwsh -NoProfile -File $tool -Definition $bad 2>&1
        $LASTEXITCODE | Should -Be 1
        ($out -join "`n") | Should -Match 'shift Ready'
    }

    It 'still lets a fully ticked item meet the Ready shift' {
        $ticked = Join-Path $TestDrive 'ticked.md'
        (Get-Content $template -Raw) -replace '\*\*Result:\*\*[^\r\n]*', '**Result:** ready' | Set-Content $ticked -Encoding utf8
        $out = & pwsh -NoProfile -File $tool -Template $ticked 2>&1
        ($out -join "`n") | Should -Match 'shift Ready'
    }

    It 'fails an empty definition' {
        $p = Join-Path $TestDrive 'empty.json'
        Set-Content $p '{}' -Encoding utf8
        $out = & pwsh -NoProfile -File $tool -Definition $p 2>&1
        $LASTEXITCODE | Should -Be 1
        ($out -join "`n") | Should -Match 'no shifts'
    }

    It 'fails a definition with a null shift among valid ones' {
        $raw = Get-Content (Join-Path $repo 'platform\development\definition-of-workflow.json') -Raw
        $p = Join-Path $TestDrive 'nullshift.json'
        ($raw -replace '"shifts"\s*:\s*\[', '"shifts": [ null,') | Set-Content $p -Encoding utf8
        $out = & pwsh -NoProfile -File $tool -Definition $p 2>&1
        $LASTEXITCODE | Should -Be 1
        ($out -join "`n") | Should -Match 'null shift'
    }

    It 'fails a shift with no checks' {
        $d = Get-Content (Join-Path $repo 'platform\development\definition-of-workflow.json') -Raw | ConvertFrom-Json
        ($d.shifts | Where-Object name -eq 'Ready').checks = @()
        $p = Join-Path $TestDrive 'nochecks.json'
        $d | ConvertTo-Json -Depth 20 | Set-Content $p -Encoding utf8
        $out = & pwsh -NoProfile -File $tool -Definition $p 2>&1
        $LASTEXITCODE | Should -Be 1
        ($out -join "`n") | Should -Match 'shift Ready: it has no checks'
    }

    It 'fails a shift whose from-state cannot be reached from the initial state' {
        $d = Get-Content (Join-Path $repo 'platform\development\definition-of-workflow.json') -Raw | ConvertFrom-Json
        $ready = $d.shifts | Where-Object name -eq 'Ready'
        $ready.from = $ready.to
        $p = Join-Path $TestDrive 'unreachable.json'
        $d | ConvertTo-Json -Depth 20 | Set-Content $p -Encoding utf8
        $out = & pwsh -NoProfile -File $tool -Definition $p 2>&1
        $LASTEXITCODE | Should -Be 1
        ($out -join "`n") | Should -Match 'is never reached'
    }

    It 'exits 2 when the definition is missing' {
        & pwsh -NoProfile -File $tool -Definition (Join-Path $TestDrive 'none.json') 2>&1 | Out-Null
        $LASTEXITCODE | Should -Be 2
    }
}
