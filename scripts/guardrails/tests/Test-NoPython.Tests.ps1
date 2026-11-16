#Requires -Modules Pester
# Martin, 2026-11-13: "At no point on any level should python ever be used."
# CI fails when the repository tracks a Python file or a workflow, script or hook calls Python.

BeforeAll {
    $script:tool = Join-Path $PSScriptRoot '..\Test-NoPython.ps1'
    function New-Repo([hashtable]$files) {
        $root = Join-Path $TestDrive ([guid]::NewGuid())
        New-Item -ItemType Directory $root | Out-Null
        git -C $root init -q
        foreach ($k in $files.Keys) {
            $p = Join-Path $root $k
            New-Item -ItemType Directory (Split-Path $p) -Force | Out-Null
            Set-Content $p $files[$k]
        }
        git -C $root add -A
        $root
    }
}

Describe 'Test-NoPython.ps1' {
    It 'passes a repository with no Python' {
        $r = New-Repo @{ 'scripts/a.ps1' = 'Write-Output 1'; '.github/workflows/ci.yml' = "steps:`n  - run: pwsh ./build.ps1" }
        & pwsh -NoProfile -File $tool -RepoRoot $r | Out-Null
        $LASTEXITCODE | Should -Be 0
    }
    It 'fails a tracked .py file' {
        $r = New-Repo @{ 'scripts/x.py' = 'print(1)' }
        $out = & pwsh -NoProfile -File $tool -RepoRoot $r
        $LASTEXITCODE | Should -Be 1
        ($out -join "`n") | Should -Match 'scripts/x.py'
    }
    It 'fails a workflow that runs <cmd>' -ForEach @(
        @{ cmd = 'python scripts/v.py' }, @{ cmd = 'pip install --quiet pyyaml' }, @{ cmd = 'uses: actions/setup-python@v5' }
    ) {
        $r = New-Repo @{ '.github/workflows/ci.yml' = "steps:`n  - run: $cmd" }
        & pwsh -NoProfile -File $tool -RepoRoot $r | Out-Null
        $LASTEXITCODE | Should -Be 1
    }
    It 'fails a PowerShell script that shells out to Python' {
        $r = New-Repo @{ 'scripts/v.ps1' = '$py = Get-Command python; & $py.Path -c "import yaml"' }
        & pwsh -NoProfile -File $tool -RepoRoot $r | Out-Null
        $LASTEXITCODE | Should -Be 1
    }
}
