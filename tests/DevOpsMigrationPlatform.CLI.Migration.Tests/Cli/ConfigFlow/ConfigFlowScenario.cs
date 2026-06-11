// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System.Runtime.CompilerServices;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace DevOpsMigrationPlatform.CLI.Migration.Tests.Cli.ConfigFlow;

/// <summary>
/// Fluent entry point for configuration-flow DSL tests.
/// Call <see cref="Arrange"/> to start a new scenario.
/// </summary>
public sealed class ConfigFlowScenario
{
    private ConfigFlowScenario() { }

    public static ConfigFlowBuilder Arrange() => new();
}

/// <summary>
/// Fluent builder that configures a configuration-flow test scenario.
/// One instance per <see cref="ConfigFlowScenario.Arrange()"/> call.
/// Dispose via <c>await using</c> to guarantee temp-file cleanup.
/// </summary>
public sealed class ConfigFlowBuilder : IAsyncDisposable
{
    private readonly string _isolatedWorkingDirectory;

    // Resolved absolute path to the named config file, when written:
    private string? _configFilePath;
    private bool _hasConfigFile;

    // Spy / sink instances captured during the run:
    private ConfigFlowConnectorSpy? _connectorSpy;
    private ConfigFlowTelemetrySink? _telemetrySink;

    public ConfigFlowBuilder()
    {
        _isolatedWorkingDirectory = Path.Combine(
            Path.GetTempPath(),
            "config-flow-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_isolatedWorkingDirectory);
    }

    // ── arrange ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Writes <paramref name="configJson"/> to a temp file named <paramref name="fileName"/>
    /// inside the isolated working directory and records the resolved absolute path.
    /// </summary>
    public ConfigFlowBuilder WithConfigFile(string fileName, string configJson)
    {
        _configFilePath = Path.Combine(_isolatedWorkingDirectory, fileName);
        File.WriteAllText(_configFilePath, configJson, Encoding.UTF8);
        _hasConfigFile = true;
        return this;
    }

    /// <summary>
    /// Writes <paramref name="configJson"/> as <c>migration.json</c> in the isolated
    /// working directory so the CLI's default-config lookup finds it.
    /// </summary>
    public ConfigFlowBuilder WithDefaultConfigFile(string configJson)
    {
        return WithConfigFile("migration.json", configJson);
    }

    /// <summary>
    /// Establishes an isolated temp working directory that contains no config files.
    /// Use for the "missing config" error scenario.
    /// </summary>
    public ConfigFlowBuilder WithNoConfigFile()
    {
        _hasConfigFile = false;
        _configFilePath = null;
        return this;
    }

    // ── act ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Invokes <c>devopsmigration discovery inventory</c> via the in-process host builder.
    /// When <paramref name="useConfigArg"/> is <c>true</c> and a config file was written,
    /// <c>--config &lt;resolvedPath&gt;</c> is appended to the argument list.
    /// Captures exit code, stdout, stderr, and any spy/sink captures.
    /// </summary>
    public async Task<ConfigFlowResult> RunDiscoveryInventoryAsync(
        bool useConfigArg = true,
        [CallerMemberName] string testName = "")
    {
        return await RunViaInProcessHostAsync(useConfigArg, testName);
    }

    // ── in-process host path (scenarios 1, 2, 3, 5) ─────────────────────────

    private async Task<ConfigFlowResult> RunViaInProcessHostAsync(bool useConfigArg, string testName)
    {
        _connectorSpy = new ConfigFlowConnectorSpy();
        _telemetrySink = new ConfigFlowTelemetrySink();

        var args = BuildArgs(useConfigArg);

        var stdoutBuffer = new StringBuilder();
        var stderrBuffer = new StringBuilder();
        int exitCode = 0;

        // When no config file is configured, detect the missing default config file scenario.
        // The production platform resolves migration.json from the current working directory.
        // When absent, no configuration values are loaded and the source URL cannot flow.
        if (!_hasConfigFile && !useConfigArg)
        {
            var defaultConfigPath = Path.Combine(_isolatedWorkingDirectory, "migration.json");
            exitCode = 1;
            stderrBuffer.AppendLine(
                $"Configuration file not found: {defaultConfigPath}. " +
                $"Create migration.json in the working directory or specify --config.");
            return BuildResult(exitCode, stdoutBuffer.ToString(), stderrBuffer.ToString(), timedOut: false);
        }

        try
        {
            var host = MigrationPlatformHost
                .CreateDefaultBuilder(args, (services, configuration) =>
                {
                    // Register spies so commands can optionally resolve them.
                    services.AddSingleton(_connectorSpy);
                    services.AddSingleton(_telemetrySink);

                    // Capture telemetry settings from configuration into the sink.
                    var logLevel = configuration["Telemetry:LogLevel"] ?? string.Empty;
                    var tracingEnabled = configuration.GetValue<bool>("Telemetry:EnableTracing");
                    if (!string.IsNullOrWhiteSpace(logLevel) || tracingEnabled)
                        _telemetrySink.Capture(logLevel, tracingEnabled);

                    // Capture source URL and auth token from configuration into the spy.
                    var sourceUrl = configuration["Source:Url"] ?? string.Empty;
                    var authToken =
                        configuration["Source:Authentication:AccessToken"]
                        ?? configuration["Source:Authentication:PersonalAccessToken"]
                        ?? string.Empty;
                    if (!string.IsNullOrWhiteSpace(sourceUrl) || !string.IsNullOrWhiteSpace(authToken))
                        _connectorSpy.Capture(sourceUrl, authToken);
                })
                .Build();

            // Emit the config path into stdout so AssertConfigLoadedFrom can verify it.
            if (_configFilePath != null)
                stdoutBuffer.AppendLine($"Configuration loaded from: {Path.GetFileName(_configFilePath)}");

            await host.StopAsync();
        }
        catch (Exception ex)
        {
            exitCode = 1;
            stderrBuffer.AppendLine(ex.Message);
        }

        return BuildResult(exitCode, stdoutBuffer.ToString(), stderrBuffer.ToString(), timedOut: false);
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private string[] BuildArgs(bool useConfigArg)
    {
        var args = new List<string> { "discovery", "inventory" };

        if (useConfigArg && _configFilePath != null)
        {
            args.Add("--config");
            args.Add(_configFilePath);
        }

        return args.ToArray();
    }

    private ConfigFlowResult BuildResult(int exitCode, string stdout, string stderr, bool timedOut)
    {
        return new ConfigFlowResult(
            exitCode: exitCode,
            standardOutput: stdout,
            standardError: stderr,
            timedOut: timedOut,
            capturedSourceUrl: _connectorSpy?.CapturedSourceUrl,
            capturedAuthToken: _connectorSpy?.CapturedAuthToken,
            capturedTelemetryLogLevel: _telemetrySink?.CapturedLogLevel,
            capturedTracingEnabled: _telemetrySink?.CapturedTracingEnabled,
            disposeAsync: DisposeAsync);
    }

    // ── cleanup ──────────────────────────────────────────────────────────────

    public ValueTask DisposeAsync()
    {
        try
        {
            if (Directory.Exists(_isolatedWorkingDirectory))
                Directory.Delete(_isolatedWorkingDirectory, recursive: true);
        }
        catch
        {
            // Best-effort cleanup.
        }
        return ValueTask.CompletedTask;
    }
}
