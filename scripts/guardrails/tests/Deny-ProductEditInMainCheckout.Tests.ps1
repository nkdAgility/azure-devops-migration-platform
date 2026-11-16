#Requires -Modules Pester
# One task = one PR = one worktree (ADR-0034). On 2026-11-16 the #169 worker built product code in
# the main checkout on main, among other sessions' work. The hook refuses any edit of product code
# (src/, tests/, docs/adr/) in the main checkout; a worktree under .claude/worktrees/ is allowed.

BeforeAll {
    $script:hook = Join-Path $PSScriptRoot '..\..\..\.claude\hooks\Deny-ProductEditInMainCheckout.ps1'
    $script:main = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..')).Path
    function Invoke-Hook([string]$path, [string]$tool = 'Write') {
        $json = @{ tool_name = $tool; tool_input = @{ file_path = $path } } | ConvertTo-Json -Compress
        $out = $json | & pwsh -NoProfile -File $hook 2>&1
        [pscustomobject]@{ Code = $LASTEXITCODE; Text = ($out -join "`n") }
    }
}

Describe 'Deny-ProductEditInMainCheckout hook' {
    It 'refuses <rel> in the main checkout' -ForEach @(
        @{ rel = 'src\DevOpsMigrationPlatform.Abstractions\Jobs\Job.cs' },
        @{ rel = 'tests\DevOpsMigrationPlatform.ControlPlane.Tests\Services\X.cs' },
        @{ rel = 'docs\adr\0035-x.md' }
    ) {
        $r = Invoke-Hook (Join-Path $main $rel)
        $r.Code | Should -Be 2
        $r.Text | Should -Match 'worktree'
    }
    It 'refuses an Edit as well as a Write' {
        (Invoke-Hook (Join-Path $main 'src\a.cs') 'Edit').Code | Should -Be 2
    }
    It 'allows the same file in a worktree' {
        (Invoke-Hook (Join-Path $main '.claude\worktrees\backlog-169\src\DevOpsMigrationPlatform.Abstractions\Jobs\Job.cs')).Code | Should -Be 0
    }
    It 'allows workflow and notes files in the main checkout' {
        (Invoke-Hook (Join-Path $main 'platform\development\control-notes.md')).Code | Should -Be 0
        (Invoke-Hook (Join-Path $main 'scripts\guardrails\x.ps1')).Code | Should -Be 0
    }
    It 'allows src/ edits when the hook runs from inside a worktree (its .git is a file)' {
        $wt = Join-Path $TestDrive 'wt'
        New-Item -ItemType Directory "$wt\.claude\hooks", "$wt\src" -Force | Out-Null
        Copy-Item $hook "$wt\.claude\hooks\"
        Set-Content "$wt\.git" 'gitdir: C:/repo/.git/worktrees/wt'
        $json = @{ tool_name = 'Write'; tool_input = @{ file_path = "$wt\src\a.cs" } } | ConvertTo-Json -Compress
        $json | & pwsh -NoProfile -File "$wt\.claude\hooks\Deny-ProductEditInMainCheckout.ps1" 2>&1 | Out-Null
        $LASTEXITCODE | Should -Be 0
    }
    It 'allows paths outside this repository' {
        (Invoke-Hook 'C:\other\repo\src\a.cs').Code | Should -Be 0
    }
}
