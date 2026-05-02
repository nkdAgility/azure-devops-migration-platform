// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Collections.Generic;
using System.IO;
using Azure.Monitor.OpenTelemetry.Exporter;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Infrastructure.Agent.Telemetry;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace DevOpsMigrationPlatform.TfsMigrationAgent
{
    /// <summary>
    /// Registers the OpenTelemetry pipeline for a TFS Migration Agent process.
    /// Handles resource attributes, metrics, and tracing in a single call so that
    /// <c>Program.cs</c> does not contain inline OTel setup logic.
    /// </summary>
    internal static class AgentOtelExtensions
    {
        /// <summary>
        /// Adds the full OpenTelemetry pipeline (resource, metrics, traces) to the service collection.
        /// </summary>
        /// <param name="services">The host service collection.</param>
        /// <param name="configuration">The host configuration (reads Telemetry:* and OTEL_* keys).</param>
        /// <param name="serviceName">The value of the <c>service.name</c> resource attribute.</param>
        /// <param name="extraActivitySources">
        /// Additional activity source names to subscribe to beyond the platform-standard ones
        /// (e.g. TFS Object Model sources). Pass <see langword="null"/> or an empty array if none.
        /// </param>
        internal static IServiceCollection AddAgentOtelPipeline(
            this IServiceCollection services,
            IConfiguration configuration,
            string serviceName,
            string[]? extraActivitySources = null)
        {
            var deploymentMode = configuration["MigrationPlatform:Environment:Type"] ?? "Standalone";
            var controlPlaneUrl = configuration["MigrationPlatform:Environment:ControlPlane:BaseUrl"]
                                  ?? "http://localhost:5100";
            var azureMonitorConnectionString = configuration["Telemetry:AzureMonitorConnectionString"];
            var hasAzureMonitor = !string.IsNullOrWhiteSpace(azureMonitorConnectionString);
            var hasOtlpEndpoint = !string.IsNullOrWhiteSpace(configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]);
            var diagnosticsPath = configuration["Telemetry:DiagnosticsPath"];
            var hasDiagnostics = !string.IsNullOrWhiteSpace(diagnosticsPath);

            if (hasDiagnostics)
            {
                if (!Path.IsPathRooted(diagnosticsPath))
                    diagnosticsPath = Path.GetFullPath(diagnosticsPath);

                var sessionId = configuration["Telemetry:DiagnosticsSessionId"]
                    ?? Environment.GetEnvironmentVariable("Telemetry__DiagnosticsSessionId");
                if (string.IsNullOrWhiteSpace(sessionId))
                {
                    sessionId = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
                    Environment.SetEnvironmentVariable("Telemetry__DiagnosticsSessionId", sessionId);
                }

                diagnosticsPath = Path.Combine(diagnosticsPath, sessionId);
            }

            var otel = services.AddOpenTelemetry();

            otel.ConfigureResource(rb => rb.AddAttributes(
                new Dictionary<string, object>
                {
                    { "service.name", serviceName },
                    { "service.namespace", WellKnownServiceNames.Namespace },
                    { WellKnownResourceAttributes.DeploymentMode, deploymentMode },
                    { WellKnownResourceAttributes.ControlPlaneUrl, controlPlaneUrl }
                }));

            otel.WithMetrics(metrics =>
            {
                metrics.AddHttpClientInstrumentation()
                       .AddRuntimeInstrumentation()
                       .AddMeter(WellKnownMeterNames.Migration)
                       .AddMeter(WellKnownMeterNames.Discovery)
                       .AddMeter(WellKnownMeterNames.ControlPlane);

                if (hasOtlpEndpoint)
                    metrics.AddOtlpExporter();
                if (hasAzureMonitor)
                    metrics.AddAzureMonitorMetricExporter(o => o.ConnectionString = azureMonitorConnectionString);
                if (hasDiagnostics)
                {
                    var metricsFile = Path.Combine(diagnosticsPath, $"{serviceName}-metrics.log");
                    var dir = Path.GetDirectoryName(metricsFile);
                    if (dir is not null)
                        Directory.CreateDirectory(dir);
                    metrics.AddReader(new PeriodicExportingMetricReader(
                        new DiagnosticsFileMetricExporter(metricsFile), exportIntervalMilliseconds: 2_000));
                }
            });

            otel.WithTracing(tracing =>
            {
                tracing.AddHttpClientInstrumentation()
                       .AddSource(WellKnownActivitySourceNames.Migration)
                       .AddSource(WellKnownActivitySourceNames.Discovery)
                       .AddSource(WellKnownActivitySourceNames.ControlPlane);

                if (extraActivitySources is not null)
                {
                    foreach (var source in extraActivitySources)
                        tracing.AddSource(source);
                }

                if (hasOtlpEndpoint)
                    tracing.AddOtlpExporter();
                if (hasAzureMonitor)
                    tracing.AddAzureMonitorTraceExporter(o => o.ConnectionString = azureMonitorConnectionString);
                if (hasDiagnostics)
                {
                    var tracesFile = Path.Combine(diagnosticsPath, $"{serviceName}-traces.log");
                    var dir = Path.GetDirectoryName(tracesFile);
                    if (dir is not null)
                        Directory.CreateDirectory(dir);
                    tracing.AddProcessor(new SimpleActivityExportProcessor(
                        new DiagnosticsFileTraceExporter(tracesFile)));
                }
            });

            return services;
        }
    }
}
