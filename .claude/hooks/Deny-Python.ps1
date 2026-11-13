#Requires -Version 7
# PreToolUse hook. Martin, 2026-11-13: "At no point on any level should python ever be used."
# Refuses any edit of a Python file and any command that runs Python or a Python tool.
# Exit 2 blocks the tool call and shows the message to the agent; exit 0 allows it.

$raw = [Console]::In.ReadToEnd()
try { $call = $raw | ConvertFrom-Json -ErrorAction Stop } catch { exit 0 }
$tool = [string]$call.tool_name
$in = $call.tool_input

$reason = $null
if ($tool -in 'Write', 'Edit', 'MultiEdit', 'NotebookEdit') {
    $path = [string]($in.file_path ?? $in.notebook_path)
    if ($path -match '\.(py|pyw|pyi|ipynb)$') { $reason = "it writes a Python file ($path)" }
}
elseif ($tool -in 'Bash', 'PowerShell') {
    $cmd = [string]$in.command
    # Text inside here-strings (PowerShell @'..'@ / @".."@) is data such as a commit message, not a command.
    $cmd = [regex]::Replace($cmd, "(?s)@'.*?'@|@`".*?`"@", '')
    # A Python tool name used as a command: at a line start, or after a separator, pipe, &, or call
    # operator. Case-sensitive, so prose such as "Python file" is not a command.
    $runner = '(?m)(^|[;&|(]|\s&|\$\()\s*(?<tool>python[0-9.]*|py|pip[0-9.]*|pipx|uv|uvx|poetry|conda|pyenv)(\.exe)?(\s|$)'
    $m = [regex]::Match($cmd, $runner)
    if ($m.Success) { $reason = "it runs $($m.Groups['tool'].Value)" }
}

if ($reason) {
    [Console]::Error.WriteLine("Refused: $reason. Python is never used, at any level (Martin, 2026-11-13). Use PowerShell 7 instead.")
    exit 2
}
exit 0
