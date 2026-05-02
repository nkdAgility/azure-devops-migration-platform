// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) NKD Agility Limited

using System.Collections.Generic;
using System.Diagnostics;
using OpenTelemetry;
using OpenTelemetry.Trace;

namespace DevOpsMigrationPlatform.Infrastructure.Tests.Telemetry;

/// <summary>
/// Shared test context for CLI OTel scenarios.
/// Holds an in-process ActivitySource, a list exporter for captured spans,
/// and the state produced by executing a simulated CLI command.
/// </summary>
internal sealed class CliOtelContext : IDisposable
{
    public ActivitySource ActivitySource { get; } = new("DevOpsMigrationPlatform.CLI");
    public List<Activity> ExportedActivities { get; } = new();
    public TracerProvider? TracerProvider { get; private set; }
    public int CommandExitCode { get; set; }
    public Activity? LastActivity { get; set; }
    public bool ExporterRegistered { get; set; }

    public void BuildTracerProvider(bool withAzureMonitorStub = true)
    {
        TracerProvider = Sdk.CreateTracerProviderBuilder()
            .AddSource("DevOpsMigrationPlatform.CLI")
            .AddInMemoryExporter(ExportedActivities)
            .Build();
        ExporterRegistered = withAzureMonitorStub;
    }

    public void Dispose()
    {
        TracerProvider?.ForceFlush(5000);
        TracerProvider?.Dispose();
        ActivitySource.Dispose();
    }
}
