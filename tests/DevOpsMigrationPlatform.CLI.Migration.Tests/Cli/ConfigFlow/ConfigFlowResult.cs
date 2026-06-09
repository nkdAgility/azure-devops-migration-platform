// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DevOpsMigrationPlatform.CLI.Migration.Tests.Cli.ConfigFlow;

/// <summary>
/// Captures all observable outputs of a configuration-flow CLI invocation.
/// Implements <see cref="IAsyncDisposable"/> so callers can use <c>await using</c>;
/// cleanup is delegated to the owning <see cref="ConfigFlowBuilder"/> via the
/// <paramref name="disposeAsync"/> callback.
/// </summary>
public sealed class ConfigFlowResult : IAsyncDisposable
{
    private readonly Func<ValueTask> _disposeAsync;

    internal ConfigFlowResult(
        int exitCode,
        string standardOutput,
        string standardError,
        bool timedOut,
        string? capturedSourceUrl,
        string? capturedAuthToken,
        string? capturedTelemetryLogLevel,
        bool? capturedTracingEnabled,
        Func<ValueTask> disposeAsync)
    {
        ExitCode = exitCode;
        StandardOutput = standardOutput;
        StandardError = standardError;
        TimedOut = timedOut;
        CapturedSourceUrl = capturedSourceUrl;
        CapturedAuthToken = capturedAuthToken;
        CapturedTelemetryLogLevel = capturedTelemetryLogLevel;
        CapturedTracingEnabled = capturedTracingEnabled;
        _disposeAsync = disposeAsync;
    }

    public int ExitCode { get; }
    public string StandardOutput { get; }
    public string StandardError { get; }
    public bool TimedOut { get; }

    // Populated by the ConnectorSpy when the CLI reaches the source connector:
    public string? CapturedSourceUrl { get; }
    public string? CapturedAuthToken { get; }

    // Populated by the TelemetrySink hook when OTel configuration is applied:
    public string? CapturedTelemetryLogLevel { get; }
    public bool? CapturedTracingEnabled { get; }

    // ── assertion extensions ──────────────────────────────────────────────

    public ConfigFlowResult AssertSucceeded()
    {
        Assert.AreEqual(0, ExitCode,
            $"Expected exit code 0 (success). Actual: {ExitCode}.\nStdout: {StandardOutput}\nStderr: {StandardError}");
        return this;
    }

    public ConfigFlowResult AssertExitCodeNonZero()
    {
        Assert.AreNotEqual(0, ExitCode,
            $"Expected non-zero exit code. Actual: {ExitCode}.\nStdout: {StandardOutput}\nStderr: {StandardError}");
        return this;
    }

    public ConfigFlowResult AssertSourceUrlReceived(string expectedUrl)
    {
        Assert.AreEqual(expectedUrl, CapturedSourceUrl,
            $"Expected source URL '{expectedUrl}' to be captured by the connector spy.");
        return this;
    }

    public ConfigFlowResult AssertAuthTokenReceived(string expectedToken)
    {
        Assert.AreEqual(expectedToken, CapturedAuthToken,
            $"Expected auth token '{expectedToken}' to be captured by the connector spy.");
        return this;
    }

    public ConfigFlowResult AssertTelemetryLogLevel(string expectedLevel)
    {
        Assert.AreEqual(expectedLevel, CapturedTelemetryLogLevel,
            StringComparer.OrdinalIgnoreCase,
            $"Expected telemetry log level '{expectedLevel}'.");
        return this;
    }

    public ConfigFlowResult AssertTracingEnabled()
    {
        Assert.IsTrue(CapturedTracingEnabled,
            "Expected OpenTelemetry tracing to be enabled.");
        return this;
    }

    public ConfigFlowResult AssertLogContains(string fragment)
    {
        var combined = StandardOutput + StandardError;
        StringAssert.Contains(combined, fragment,
            $"Expected output to contain '{fragment}'.\nStdout: {StandardOutput}\nStderr: {StandardError}");
        return this;
    }

    public ConfigFlowResult AssertConfigLoadedFrom(string fileName)
    {
        var combined = StandardOutput + StandardError;
        StringAssert.Contains(combined, fileName,
            $"Expected output to contain config file name '{fileName}'.\nStdout: {StandardOutput}\nStderr: {StandardError}");
        return this;
    }

    public ValueTask DisposeAsync() => _disposeAsync();
}
