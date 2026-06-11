// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

namespace DevOpsMigrationPlatform.CLI.Migration.Tests.Cli.ConfigFlow;

/// <summary>
/// Captures OTel configuration applied during the CLI host initialisation so tests can
/// assert that Telemetry settings from the config file reach the OpenTelemetry layer.
/// </summary>
internal sealed class ConfigFlowTelemetrySink
{
    public string? CapturedLogLevel { get; private set; }
    public bool? CapturedTracingEnabled { get; private set; }

    public void Capture(string logLevel, bool tracingEnabled)
    {
        CapturedLogLevel = logLevel;
        CapturedTracingEnabled = tracingEnabled;
    }
}
