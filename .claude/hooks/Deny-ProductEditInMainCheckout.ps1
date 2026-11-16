#Requires -Version 7
# PreToolUse hook. One task = one PR = one worktree (ADR-0034). Refuses any edit of product code
# (src/, tests/, docs/adr/) in this repository's main checkout; work in a worktree under
# .claude/worktrees/ on a backlog/<issue>-<slug> branch instead. Exit 2 blocks; exit 0 allows.

$raw = [Console]::In.ReadToEnd()
try { $call = $raw | ConvertFrom-Json -ErrorAction Stop } catch { exit 0 }
if ($call.tool_name -notin 'Write', 'Edit', 'MultiEdit', 'NotebookEdit') { exit 0 }
$path = [string]($call.tool_input.file_path ?? $call.tool_input.notebook_path)
if (-not $path) { exit 0 }

$main = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..')).TrimEnd('\', '/')
# In a worktree, .git is a file pointing at the main repository: this copy of the hook is in a
# worktree, where product edits belong.
if (Test-Path -LiteralPath (Join-Path $main '.git') -PathType Leaf) { exit 0 }
$full = [IO.Path]::GetFullPath($path)
$sep = [IO.Path]::DirectorySeparatorChar
if (-not $full.StartsWith($main + $sep, [StringComparison]::OrdinalIgnoreCase)) { exit 0 }

$rel = $full.Substring($main.Length + 1) -replace '\\', '/'
if ($rel -match '^\.claude/worktrees/') { exit 0 }
if ($rel -match '^(src|tests|docs/adr)/') {
    [Console]::Error.WriteLine("Refused: $rel is product code in the main checkout. One task = one PR = one worktree (ADR-0034): create a worktree under .claude/worktrees/ on a backlog/<issue>-<slug> branch and edit it there.")
    exit 2
}
exit 0
