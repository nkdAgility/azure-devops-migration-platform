// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System.Diagnostics;

namespace DevOpsMigrationPlatform.Testing.Dsl.SystemTests;

/// <summary>
/// Fluent builder for a dotnet-test subprocess invocation.
/// </summary>
public sealed class DotnetTestRunnerBuilder
{
    private readonly string _projectPath;
    private string? _filter;
    private TimeSpan _timeout = TimeSpan.FromMinutes(5);

    private DotnetTestRunnerBuilder(string projectPath)
    {
        if (string.IsNullOrWhiteSpace(projectPath))
        {
            throw new ArgumentNullException(nameof(projectPath));
        }

        _projectPath = projectPath;
    }

    /// <summary>Creates a builder targeting the given project path.</summary>
    public static DotnetTestRunnerBuilder AgainstProject(string projectPath)
        => new(projectPath);

    /// <summary>Applies a --filter expression.</summary>
    public DotnetTestRunnerBuilder WithFilter(string filter)
    {
        _filter = filter;
        return this;
    }

    /// <summary>Overrides the default process timeout.</summary>
    public DotnetTestRunnerBuilder WithTimeout(TimeSpan timeout)
    {
        _timeout = timeout;
        return this;
    }

    /// <summary>Executes dotnet test and returns the captured result.</summary>
    public async Task<DotnetTestResult> RunAsync()
    {
        var args = $"test \"{_projectPath}\"";
        if (!string.IsNullOrEmpty(_filter))
        {
            args += $" --filter \"{_filter}\"";
        }

        var psi = new ProcessStartInfo("dotnet", args)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        var sw = Stopwatch.StartNew();
        using var process = new Process { StartInfo = psi };

        var stdOut = new System.Text.StringBuilder();
        var stdErr = new System.Text.StringBuilder();

        process.OutputDataReceived += (_, e) => { if (e.Data is not null) { stdOut.AppendLine(e.Data); } };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) { stdErr.AppendLine(e.Data); } };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        var completed = await Task.Run(() => process.WaitForExit((int)_timeout.TotalMilliseconds));

        sw.Stop();

        if (!completed)
        {
            process.Kill(entireProcessTree: true);
        }

        return new DotnetTestResult(
            exitCode: process.ExitCode,
            stdOut: stdOut.ToString(),
            stdErr: stdErr.ToString(),
            elapsed: sw.Elapsed);
    }
}
