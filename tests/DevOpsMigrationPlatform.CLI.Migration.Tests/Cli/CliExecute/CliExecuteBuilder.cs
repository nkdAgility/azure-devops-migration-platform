// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System.Text;
using DevOpsMigrationPlatform.CLI.Migration.Tests.TestUtilities;

namespace DevOpsMigrationPlatform.CLI.Migration.Tests.Cli.CliExecute;

/// <summary>
/// Fluent builder that configures and runs a CLI command execution safety test.
/// Dispose via <c>await using</c> to guarantee any future resource cleanup.
/// </summary>
public sealed class CliExecuteBuilder : IAsyncDisposable
{
    private string? _configArg;
    private bool _helpFlag;
    private bool _noRequiredParams;
    private string? _helpCommand;

    // ── arrange ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Configures the <c>--config</c> argument to point to a path that does not exist on disk.
    /// The supplied <paramref name="invalidPath"/> is used verbatim; no file is created.
    /// </summary>
    public CliExecuteBuilder WithInvalidConfigPath(string invalidPath)
    {
        _configArg = invalidPath;
        return this;
    }

    /// <summary>
    /// Configures the invocation to omit all required parameters for the target command,
    /// exercising the CLI's argument-validation error path.
    /// </summary>
    public CliExecuteBuilder WithNoRequiredParameters()
    {
        _noRequiredParams = true;
        return this;
    }

    /// <summary>
    /// Appends <c>--help</c> to the argument list for the named subcommand.
    /// </summary>
    /// <param name="command">
    /// Space-separated command tokens, e.g. <c>"discovery inventory"</c>.
    /// </param>
    public CliExecuteBuilder WithHelpFlag(string command)
    {
        _helpFlag = true;
        _helpCommand = command;
        return this;
    }

    // ── act ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// Runs the configured CLI invocation in-process via <c>MigrationPlatformHost</c>.
    /// Captures exit code, stdout, stderr, and exception state.
    /// Use for error-path scenarios where the host startup fails fast (scenarios 1 and 3).
    /// </summary>
    public async Task<CliExecuteResult> RunInProcessAsync()
    {
        var args = BuildArgs();
        var stderrBuffer = new StringBuilder();
        int exitCode = 0;

        try
        {
            var host = MigrationPlatformHost
                .CreateDefaultBuilder(args, (services, configuration) => { })
                .Build();

            await host.StopAsync();
        }
        catch (Exception ex)
        {
            exitCode = 1;
            stderrBuffer.AppendLine(ex.Message);
        }

        return new CliExecuteResult(
            exitCode: exitCode,
            standardOutput: string.Empty,
            standardError: stderrBuffer.ToString(),
            timedOut: false);
    }

    /// <summary>
    /// Runs the configured CLI invocation out-of-process via <c>CliRunner</c>.
    /// Captures exit code, stdout, stderr, and timeout state.
    /// Use for scenarios where the process must exit before host startup (scenario 2, --help).
    /// </summary>
    public async Task<CliExecuteResult> RunOutOfProcessAsync()
    {
        var args = BuildArgs();
        var cliResult = await CliRunner.RunAsync(args);

        return new CliExecuteResult(
            exitCode: cliResult.ExitCode,
            standardOutput: cliResult.StandardOutput,
            standardError: cliResult.StandardError,
            timedOut: cliResult.TimedOut);
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    private string[] BuildArgs()
    {
        var args = new List<string>();

        if (_helpFlag && _helpCommand != null)
        {
            // Split "discovery inventory" into tokens and append --help
            args.AddRange(_helpCommand.Split(' ', StringSplitOptions.RemoveEmptyEntries));
            args.Add("--help");
        }
        else if (_configArg != null)
        {
            // Run discovery inventory with the invalid config path
            args.AddRange(["discovery", "inventory", "--config", _configArg]);
        }
        else if (_noRequiredParams)
        {
            // Run discovery inventory with no required parameters
            args.AddRange(["discovery", "inventory"]);
        }

        return args.ToArray();
    }

    // ── cleanup ───────────────────────────────────────────────────────────────

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
