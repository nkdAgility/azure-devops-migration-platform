// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) NKD Agility Limited

#if !NETFRAMEWORK
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.IO;
using System.Linq;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace DevOpsMigrationPlatform.Infrastructure.Tests.Telemetry;

/// <summary>
/// Verifies that the file diagnostic exporters write trace and metric data
/// to physical files on disk when wired into the OTel pipeline.
/// </summary>
[TestClass]
public class FileDiagnosticExporterTests
{
    private string _tempDir = null!;

    [TestInitialize]
    public void Setup()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "otel-diag-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [TestMethod]
    public void FileTraceExporter_WritesSpansToFile()
    {
        var tracesFile = Path.Combine(_tempDir, "test-traces.log");

        // Scoped block ensures the provider + exporter are disposed (releasing the file)
        // before we read the file.
        using (var tracerProvider = Sdk.CreateTracerProviderBuilder()
            .AddSource(WellKnownActivitySourceNames.Migration)
            .AddProcessor(new SimpleActivityExportProcessor(new FileTraceExporter(tracesFile)))
            .Build())
        {
            using var source = new ActivitySource(WellKnownActivitySourceNames.Migration);
            using (var activity = source.StartActivity("file.export.test"))
            {
                activity?.SetTag("testKey", "testValue");
            }

            tracerProvider!.ForceFlush();
        }

        Assert.IsTrue(File.Exists(tracesFile), "Traces file should be created.");
        var content = File.ReadAllText(tracesFile);
        Assert.IsTrue(content.Contains("file.export.test"),
            "Traces file should contain the span display name.");
        Assert.IsTrue(content.Contains("testKey=testValue"),
            "Traces file should contain span tags.");
    }

    [TestMethod]
    public void FileMetricExporter_WritesMetricsToFile()
    {
        var metricsFile = Path.Combine(_tempDir, "test-metrics.log");

        using (var meterProvider = Sdk.CreateMeterProviderBuilder()
            .AddMeter(WellKnownMeterNames.Migration)
            .AddReader(new PeriodicExportingMetricReader(new FileMetricExporter(metricsFile), exportIntervalMilliseconds: 1000))
            .Build())
        {
            using var meter = new Meter(WellKnownMeterNames.Migration);
            var counter = meter.CreateCounter<long>("file.export.test.counter");
            counter.Add(99);

            meterProvider!.ForceFlush();
        }

        Assert.IsTrue(File.Exists(metricsFile), "Metrics file should be created.");
        var content = File.ReadAllText(metricsFile);
        Assert.IsTrue(content.Contains("file.export.test.counter"),
            "Metrics file should contain the metric name.");
        Assert.IsTrue(content.Contains("99"),
            "Metrics file should contain the metric value.");
    }

    [TestMethod]
    public void FileDiagnosticsExtensions_AddFileExporter_Tracing_WritesToDisk()
    {
        var tracesFile = Path.Combine(_tempDir, "test-svc-traces.log");

        using (var tracerProvider = Sdk.CreateTracerProviderBuilder()
            .AddSource(WellKnownActivitySourceNames.ControlPlane)
            .AddFileExporter(_tempDir, "test-svc")
            .Build())
        {
            using var source = new ActivitySource(WellKnownActivitySourceNames.ControlPlane);
            using (source.StartActivity("ext.test.span")) { }

            tracerProvider!.ForceFlush();
        }

        Assert.IsTrue(File.Exists(tracesFile), "Extension method should create traces file.");
        var content = File.ReadAllText(tracesFile);
        Assert.IsTrue(content.Contains("ext.test.span"),
            "Extension-created traces file should contain the span.");
    }

    [TestMethod]
    public void FileDiagnosticsExtensions_AddFileExporter_Metrics_WritesToDisk()
    {
        var metricsFile = Path.Combine(_tempDir, "test-svc-metrics.log");

        using (var meterProvider = Sdk.CreateMeterProviderBuilder()
            .AddMeter(WellKnownMeterNames.Discovery)
            .AddFileExporter(_tempDir, "test-svc")
            .Build())
        {
            using var meter = new Meter(WellKnownMeterNames.Discovery);
            var counter = meter.CreateCounter<long>("ext.test.counter");
            counter.Add(7);

            meterProvider!.ForceFlush();
        }

        Assert.IsTrue(File.Exists(metricsFile), "Extension method should create metrics file.");
        var content = File.ReadAllText(metricsFile);
        Assert.IsTrue(content.Contains("ext.test.counter"),
            "Extension-created metrics file should contain the metric.");
    }

    [TestMethod]
    public void FileTraceExporter_CreatesDirectoryIfMissing()
    {
        var nestedDir = Path.Combine(_tempDir, "nested", "deep");
        var tracesFile = Path.Combine(nestedDir, "traces.log");

        using var exporter = new FileTraceExporter(tracesFile);

        Assert.IsTrue(Directory.Exists(nestedDir),
            "File exporter should create the directory structure on construction.");
    }

    [TestMethod]
    public void GetDiagnosticsPath_ReturnsNull_WhenNotConfigured()
    {
        var config = new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build();
        var result = FileDiagnosticsExtensions.GetDiagnosticsPath(config);
        Assert.IsNull(result, "Should return null when Telemetry:DiagnosticsPath is not set.");
    }

    [TestMethod]
    public void GetDiagnosticsPath_ResolvesRelativePath()
    {
        // Use environment variables to simulate configuration since
        // Microsoft.Extensions.Configuration.Memory is not available.
        var envKey = "Telemetry__DiagnosticsPath";
        var originalValue = Environment.GetEnvironmentVariable(envKey);
        try
        {
            Environment.SetEnvironmentVariable(envKey, "otel-output");
            var config = new Microsoft.Extensions.Configuration.ConfigurationBuilder()
                .AddEnvironmentVariables()
                .Build();

            var result = FileDiagnosticsExtensions.GetDiagnosticsPath(config);
            Assert.IsNotNull(result);
            Assert.IsTrue(Path.IsPathRooted(result), "Relative path should be resolved to an absolute path.");
            Assert.IsTrue(result!.Contains("otel-output"), "Resolved path should contain the configured folder name.");
        }
        finally
        {
            Environment.SetEnvironmentVariable(envKey, originalValue);
        }
    }

    [TestMethod]
    public void GetDiagnosticsPath_UsesSessionIdSubfolder()
    {
        var pathKey = "Telemetry__DiagnosticsPath";
        var sessionKey = "Telemetry__DiagnosticsSessionId";
        var origPath = Environment.GetEnvironmentVariable(pathKey);
        var origSession = Environment.GetEnvironmentVariable(sessionKey);
        try
        {
            Environment.SetEnvironmentVariable(pathKey, _tempDir);
            Environment.SetEnvironmentVariable(sessionKey, "test-session-42");
            var config = new Microsoft.Extensions.Configuration.ConfigurationBuilder()
                .AddEnvironmentVariables()
                .Build();

            var result = FileDiagnosticsExtensions.GetDiagnosticsPath(config);
            Assert.IsNotNull(result);
            Assert.IsTrue(result!.EndsWith("test-session-42"),
                "Path should end with the explicit session ID.");
        }
        finally
        {
            Environment.SetEnvironmentVariable(pathKey, origPath);
            Environment.SetEnvironmentVariable(sessionKey, origSession);
        }
    }

    [TestMethod]
    public void GetDiagnosticsPath_GeneratesTimestampSessionId_WhenNotConfigured()
    {
        var pathKey = "Telemetry__DiagnosticsPath";
        var sessionKey = "Telemetry__DiagnosticsSessionId";
        var origPath = Environment.GetEnvironmentVariable(pathKey);
        var origSession = Environment.GetEnvironmentVariable(sessionKey);
        try
        {
            Environment.SetEnvironmentVariable(pathKey, _tempDir);
            Environment.SetEnvironmentVariable(sessionKey, null);
            var config = new Microsoft.Extensions.Configuration.ConfigurationBuilder()
                .AddEnvironmentVariables()
                .Build();

            var result = FileDiagnosticsExtensions.GetDiagnosticsPath(config);
            Assert.IsNotNull(result);
            // Session subfolder should be a timestamp like 20260427-123456
            var sessionFolder = Path.GetFileName(result!);
            Assert.IsTrue(sessionFolder.Length == 15 && sessionFolder[8] == '-',
                $"Auto-generated session ID should be yyyyMMdd-HHmmss format, got: {sessionFolder}");
        }
        finally
        {
            Environment.SetEnvironmentVariable(pathKey, origPath);
            Environment.SetEnvironmentVariable(sessionKey, origSession);
        }
    }

    // ─────────────────────────────────────────────────────────────────────
    // FileLoggerProvider unit test
    // ─────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void FileLoggerProvider_WritesLogEntriesToFile()
    {
        var logFile = Path.Combine(_tempDir, "test-logs.log");

        using (var provider = new FileLoggerProvider(logFile))
        {
            var logger = provider.CreateLogger("TestCategory");
            logger.LogInformation("Hello from test");
            logger.LogWarning("A warning");
        }

        Assert.IsTrue(File.Exists(logFile), "Log file should be created.");
        var content = File.ReadAllText(logFile);
        Assert.IsTrue(content.Contains("[Information] TestCategory: Hello from test"),
            "Log file should contain the information message.");
        Assert.IsTrue(content.Contains("[Warning] TestCategory: A warning"),
            "Log file should contain the warning message.");
    }

    [TestMethod]
    public void FileLoggerProvider_SkipsDebugAndTrace()
    {
        var logFile = Path.Combine(_tempDir, "test-filter-logs.log");

        using (var provider = new FileLoggerProvider(logFile))
        {
            var logger = provider.CreateLogger("FilterTest");
            logger.LogDebug("debug msg");
            logger.LogTrace("trace msg");
            logger.LogInformation("info msg");
        }

        var content = File.ReadAllText(logFile);
        Assert.IsFalse(content.Contains("debug msg"), "Debug should be filtered out.");
        Assert.IsFalse(content.Contains("trace msg"), "Trace should be filtered out.");
        Assert.IsTrue(content.Contains("info msg"), "Information should be written.");
    }

    // ─────────────────────────────────────────────────────────────────────
    // Per-app wiring: verify each service name produces all 3 file types
    // ─────────────────────────────────────────────────────────────────────

    [TestMethod]
    [DataRow(WellKnownServiceNames.Cli)]
    [DataRow(WellKnownServiceNames.ControlPlaneHost)]
    [DataRow(WellKnownServiceNames.MigrationAgent)]
    [DataRow(WellKnownServiceNames.TfsMigrationAgent)]
    public void AllThreeSignals_AreWritten_ForServiceName(string serviceName)
    {
        var tracesFile = Path.Combine(_tempDir, $"{serviceName}-traces.log");
        var metricsFile = Path.Combine(_tempDir, $"{serviceName}-metrics.log");
        var logsFile = Path.Combine(_tempDir, $"{serviceName}-logs.log");

        // Traces
        using (var tp = Sdk.CreateTracerProviderBuilder()
            .AddSource(WellKnownActivitySourceNames.Migration)
            .AddFileExporter(_tempDir, serviceName)
            .Build())
        {
            using var source = new ActivitySource(WellKnownActivitySourceNames.Migration);
            using (var activity = source.StartActivity($"test.{serviceName}.span"))
            {
                activity?.SetTag("service", serviceName);
            }
            tp!.ForceFlush();
        }

        // Metrics
        using (var mp = Sdk.CreateMeterProviderBuilder()
            .AddMeter(WellKnownMeterNames.Migration)
            .AddFileExporter(_tempDir, serviceName)
            .Build())
        {
            using var meter = new Meter(WellKnownMeterNames.Migration);
            var counter = meter.CreateCounter<long>($"test.{serviceName}.counter");
            counter.Add(42);
            mp!.ForceFlush();
        }

        // Logs
        using (var provider = new FileLoggerProvider(logsFile))
        {
            var logger = provider.CreateLogger($"Test.{serviceName}");
            logger.LogInformation($"Diagnostic log from {serviceName}");
        }

        // Assert all 3 files exist and contain expected data
        Assert.IsTrue(File.Exists(tracesFile), $"{serviceName}-traces.log should exist.");
        var traces = File.ReadAllText(tracesFile);
        Assert.IsTrue(traces.Contains($"test.{serviceName}.span"),
            $"{serviceName}-traces.log should contain the span.");

        Assert.IsTrue(File.Exists(metricsFile), $"{serviceName}-metrics.log should exist.");
        var metrics = File.ReadAllText(metricsFile);
        Assert.IsTrue(metrics.Contains($"test.{serviceName}.counter"),
            $"{serviceName}-metrics.log should contain the metric.");

        Assert.IsTrue(File.Exists(logsFile), $"{serviceName}-logs.log should exist.");
        var logs = File.ReadAllText(logsFile);
        Assert.IsTrue(logs.Contains($"Diagnostic log from {serviceName}"),
            $"{serviceName}-logs.log should contain the log entry.");
    }
}
#endif
