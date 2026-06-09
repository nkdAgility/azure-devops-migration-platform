// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System.Diagnostics;

namespace DevOpsMigrationPlatform.Testing.Dsl.SystemTests;

/// <summary>
/// Runs the inventory CLI subcommand as a subprocess and captures its output.
/// Used by CI-execution scenarios that assert against real CLI behaviour rather
/// than a dotnet-test sub-run.
/// </summary>
public sealed class InventoryCliRunner
{
    private readonly string _cliProjectPath;
    private string? _orgUrl;
    private string? _pat;
    private TimeSpan _timeout = TimeSpan.FromMinutes(2);

    private InventoryCliRunner(string cliProjectPath) => _cliProjectPath = cliProjectPath;

    /// <summary>Creates a runner targeting the given CLI project path.</summary>
    public static InventoryCliRunner AgainstProject(string cliProjectPath)
        => new(cliProjectPath);

    /// <summary>Sets the Azure DevOps organisation URL passed as env var to the subprocess.</summary>
    public InventoryCliRunner WithOrg(string orgUrl) { _orgUrl = orgUrl; return this; }

    /// <summary>Sets the PAT passed as env var to the subprocess.</summary>
    public InventoryCliRunner WithPat(string pat) { _pat = pat; return this; }

    /// <summary>Overrides the default process timeout.</summary>
    public InventoryCliRunner WithTimeout(TimeSpan timeout) { _timeout = timeout; return this; }

    /// <summary>Runs `dotnet run --project {path} -- inventory` and returns the captured result.</summary>
    public async Task<DotnetTestResult> RunInventoryAsync()
    {
        var args = $"run --project \"{_cliProjectPath}\" -- inventory";

        var psi = new ProcessStartInfo("dotnet", args)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        if (_orgUrl is not null)
            psi.Environment["AZDEVOPS_SYSTEM_TEST_ORG"] = _orgUrl;
        if (_pat is not null)
            psi.Environment["AZDEVOPS_SYSTEM_TEST_PAT"] = _pat;

        var sw = Stopwatch.StartNew();
        using var process = new Process { StartInfo = psi };
        var stdOut = new System.Text.StringBuilder();
        var stdErr = new System.Text.StringBuilder();

        process.OutputDataReceived += (_, e) => { if (e.Data is not null) stdOut.AppendLine(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) stdErr.AppendLine(e.Data); };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        var completed = await Task.Run(() => process.WaitForExit((int)_timeout.TotalMilliseconds));
        sw.Stop();

        if (!completed)
            process.Kill(entireProcessTree: true);

        return new DotnetTestResult(
            exitCode: process.ExitCode,
            stdOut: stdOut.ToString(),
            stdErr: stdErr.ToString(),
            elapsed: sw.Elapsed);
    }
}
