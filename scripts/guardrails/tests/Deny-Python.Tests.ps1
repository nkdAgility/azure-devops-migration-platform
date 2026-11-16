#Requires -Modules Pester
# Martin, 2026-11-13: "At no point on any level should python ever be used."
# The hook refuses any agent edit of a Python file and any command that runs Python or its tools.

BeforeAll {
    $script:hook = Join-Path $PSScriptRoot '..\..\..\.claude\hooks\Deny-Python.ps1'
    function Invoke-Hook([hashtable]$payload) {
        $json = $payload | ConvertTo-Json -Depth 5 -Compress
        $out = $json | & pwsh -NoProfile -File $hook 2>&1
        [pscustomobject]@{ Code = $LASTEXITCODE; Text = ($out -join "`n") }
    }
}

Describe 'Deny-Python hook' {
    It 'refuses writing a .py file' {
        $r = Invoke-Hook @{ tool_name = 'Write'; tool_input = @{ file_path = 'C:\repo\scripts\x.py'; content = 'print(1)' } }
        $r.Code | Should -Be 2
        $r.Text | Should -Match 'Python'
    }
    It 'refuses editing a notebook' {
        (Invoke-Hook @{ tool_name = 'Edit'; tool_input = @{ file_path = 'a/b.ipynb' } }).Code | Should -Be 2
    }
    It 'refuses a command that runs <cmd>' -ForEach @(
        @{ cmd = 'python scripts/x.py' }, @{ cmd = 'python3 -c "print(1)"' }, @{ cmd = 'py -3 x' },
        @{ cmd = 'pip install pyyaml' }, @{ cmd = 'git status; pip3 install x' }, @{ cmd = 'uv run x' }
    ) {
        (Invoke-Hook @{ tool_name = 'PowerShell'; tool_input = @{ command = $cmd } }).Code | Should -Be 2
        (Invoke-Hook @{ tool_name = 'Bash'; tool_input = @{ command = $cmd } }).Code | Should -Be 2
    }
    It 'allows ordinary edits and commands' {
        (Invoke-Hook @{ tool_name = 'Write'; tool_input = @{ file_path = 'scripts/x.ps1' } }).Code | Should -Be 0
        (Invoke-Hook @{ tool_name = 'PowerShell'; tool_input = @{ command = 'git log --oneline -1' } }).Code | Should -Be 0
        (Invoke-Hook @{ tool_name = 'Bash'; tool_input = @{ command = 'echo copy.py.bak is not a run' } }).Code | Should -Be 0
    }
    It 'allows a commit message that talks about Python' {
        $msg = "git commit -m @'`nLock out Python: hook`n  Python file and any command running python/py/pip`n'@ -- a.ps1"
        (Invoke-Hook @{ tool_name = 'PowerShell'; tool_input = @{ command = $msg } }).Code | Should -Be 0
    }
    It 'names the refused tool in its message' {
        (Invoke-Hook @{ tool_name = 'PowerShell'; tool_input = @{ command = 'pip install x' } }).Text | Should -Match 'runs pip'
    }
}
