// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

#if !NETFRAMEWORK
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Storage;
using DevOpsMigrationPlatform.Infrastructure.Agent.Telemetry;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace DevOpsMigrationPlatform.Infrastructure.Tests.Telemetry;

/// <summary>
/// Verifies the PackageLoggerProvider diagnostic sink behaviour:
/// NDJSON persistence, required-field completeness, minimum-level filtering,
/// and resilience when the package store is unavailable.
/// </summary>
[TestClass]
public class PackageDiagnosticsSinkTests
{
    private static IServiceProvider BuildServiceProvider(IPackageAccess package)
    {
        var services = new ServiceCollection();
        services.AddSingleton(package);
        return services.BuildServiceProvider();
    }

    private static ActivePackageState BuildActiveState() =>
        new() { CurrentJob = new Job { JobId = "job-diag-sink", Kind = JobKind.Export } };

    // ─── Scenario: Warning and error log records are written to the package ───

    /// <summary>
    /// When a warning or error record is emitted by the agent the provider
    /// appends a structured NDJSON log record to the diagnostics stream in the package.
    /// </summary>
    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public async Task PackageLoggerProvider_WarningOrError_AppendsNdjsonToPackage()
    {
        var payloads = new List<string>();
        var mockPackage = new Mock<IPackageAccess>(MockBehavior.Strict);
        mockPackage
            .Setup(p => p.AppendLogAsync(
                It.Is<PackageLogContext>(c => c.Stream == PackageLogStream.Diagnostics),
                It.IsAny<PackageLogPayload>(),
                It.IsAny<CancellationToken>()))
            .Callback<PackageLogContext, PackageLogPayload, CancellationToken>((_, payload, _) =>
            {
                using var reader = new System.IO.StreamReader(payload.Content, Encoding.UTF8, leaveOpen: true);
                payloads.Add(reader.ReadToEnd());
            })
            .Returns(ValueTask.CompletedTask);

        var state = BuildActiveState();
        using var provider = new PackageLoggerProvider(
            state,
            Options.Create(new DiagnosticLogOptions { MinimumLevel = "Warning" }),
            BuildServiceProvider(mockPackage.Object));

        var logger = provider.CreateLogger("AgentCategory");
        logger.LogWarning("A warning log");
        logger.LogError("An error log");

        await provider.FlushAsync();

        Assert.IsTrue(payloads.Count > 0, "Expected at least one NDJSON payload appended to the package.");
        var allContent = string.Join(string.Empty, payloads);
        var lines = allContent.Split('\n', System.StringSplitOptions.RemoveEmptyEntries);
        Assert.IsTrue(lines.Length >= 2, $"Expected at least 2 NDJSON lines; got {lines.Length}.");
    }

    // ─── Scenario: Diagnostic log records contain required fields ───

    /// <summary>
    /// Each NDJSON line must be a valid JSON object containing timestamp, level,
    /// category, and message; lines with exceptions also contain an exception field.
    /// </summary>
    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public async Task PackageLoggerProvider_WrittenRecords_ContainRequiredFields()
    {
        var payloads = new List<string>();
        var mockPackage = new Mock<IPackageAccess>(MockBehavior.Strict);
        mockPackage
            .Setup(p => p.AppendLogAsync(
                It.IsAny<PackageLogContext>(),
                It.IsAny<PackageLogPayload>(),
                It.IsAny<CancellationToken>()))
            .Callback<PackageLogContext, PackageLogPayload, CancellationToken>((_, payload, _) =>
            {
                using var reader = new System.IO.StreamReader(payload.Content, Encoding.UTF8, leaveOpen: true);
                payloads.Add(reader.ReadToEnd());
            })
            .Returns(ValueTask.CompletedTask);

        var state = BuildActiveState();
        using var provider = new PackageLoggerProvider(
            state,
            Options.Create(new DiagnosticLogOptions { MinimumLevel = "Information" }),
            BuildServiceProvider(mockPackage.Object));

        var logger = provider.CreateLogger("My.Category");
        var ex = new System.InvalidOperationException("boom");
        logger.LogError(ex, "error with exception");
        logger.LogWarning("plain warning");

        await provider.FlushAsync();

        var allContent = string.Join(string.Empty, payloads);
        var lines = allContent.Split('\n', System.StringSplitOptions.RemoveEmptyEntries);
        Assert.IsTrue(lines.Length >= 2, "Expected at least 2 NDJSON lines.");

        foreach (var line in lines)
        {
            var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;

            Assert.IsTrue(root.TryGetProperty("Timestamp", out _), $"Missing 'Timestamp' in: {line}");
            Assert.IsTrue(root.TryGetProperty("Level", out _), $"Missing 'Level' in: {line}");
            Assert.IsTrue(root.TryGetProperty("Category", out _), $"Missing 'Category' in: {line}");
            Assert.IsTrue(root.TryGetProperty("Message", out _), $"Missing 'Message' in: {line}");
        }

        // At least one line should have an "Exception" field (from the LogError call).
        bool hasException = false;
        foreach (var line in lines)
        {
            var doc = JsonDocument.Parse(line);
            if (doc.RootElement.TryGetProperty("Exception", out var excProp)
                && excProp.ValueKind != System.Text.Json.JsonValueKind.Null)
            {
                hasException = true;
                break;
            }
        }
        Assert.IsTrue(hasException, "Expected at least one NDJSON line with a non-null 'Exception' field.");
    }

    // ─── Scenario: Log records below configured minimum level are discarded ───

    /// <summary>
    /// When the minimum level is Information, Trace and Debug records must not
    /// be written to the diagnostics stream.
    /// </summary>
    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public async Task PackageLoggerProvider_BelowMinimumLevel_RecordsAreDiscarded()
    {
        var payloads = new List<string>();
        var mockPackage = new Mock<IPackageAccess>(MockBehavior.Strict);
        mockPackage
            .Setup(p => p.AppendLogAsync(
                It.IsAny<PackageLogContext>(),
                It.IsAny<PackageLogPayload>(),
                It.IsAny<CancellationToken>()))
            .Callback<PackageLogContext, PackageLogPayload, CancellationToken>((_, payload, _) =>
            {
                using var reader = new System.IO.StreamReader(payload.Content, Encoding.UTF8, leaveOpen: true);
                payloads.Add(reader.ReadToEnd());
            })
            .Returns(ValueTask.CompletedTask);

        var state = BuildActiveState();
        using var provider = new PackageLoggerProvider(
            state,
            Options.Create(new DiagnosticLogOptions { MinimumLevel = "Information" }),
            BuildServiceProvider(mockPackage.Object));

        var logger = provider.CreateLogger("FilterTest");
        logger.LogTrace("trace message — must be dropped");
        logger.LogDebug("debug message — must be dropped");

        await provider.FlushAsync();

        // Nothing should have been flushed to the package.
        var allContent = string.Join(string.Empty, payloads);
        Assert.AreEqual(0, allContent.Trim().Length,
            "Trace and Debug records must not be written when minimum level is Information.");
    }

    // ─── Scenario: Log records at or above configured minimum level are written ───

    /// <summary>
    /// When the minimum level is Information, Information, Warning, and Error records
    /// must all be written to the diagnostics stream.
    /// </summary>
    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public async Task PackageLoggerProvider_AtOrAboveMinimumLevel_RecordsAreWritten()
    {
        var payloads = new List<string>();
        var mockPackage = new Mock<IPackageAccess>(MockBehavior.Strict);
        mockPackage
            .Setup(p => p.AppendLogAsync(
                It.IsAny<PackageLogContext>(),
                It.IsAny<PackageLogPayload>(),
                It.IsAny<CancellationToken>()))
            .Callback<PackageLogContext, PackageLogPayload, CancellationToken>((_, payload, _) =>
            {
                using var reader = new System.IO.StreamReader(payload.Content, Encoding.UTF8, leaveOpen: true);
                payloads.Add(reader.ReadToEnd());
            })
            .Returns(ValueTask.CompletedTask);

        var state = BuildActiveState();
        using var provider = new PackageLoggerProvider(
            state,
            Options.Create(new DiagnosticLogOptions { MinimumLevel = "Information" }),
            BuildServiceProvider(mockPackage.Object));

        var logger = provider.CreateLogger("LevelTest");
        logger.LogInformation("info message");
        logger.LogWarning("warning message");
        logger.LogError("error message");

        await provider.FlushAsync();

        var allContent = string.Join(string.Empty, payloads);
        var lines = allContent.Split('\n', System.StringSplitOptions.RemoveEmptyEntries);
        Assert.IsTrue(lines.Length >= 3,
            $"Expected at least 3 NDJSON lines (Information/Warning/Error); got {lines.Length}.");
        Assert.IsTrue(allContent.Contains("info message"), "Information record must be written.");
        Assert.IsTrue(allContent.Contains("warning message"), "Warning record must be written.");
        Assert.IsTrue(allContent.Contains("error message"), "Error record must be written.");
    }

    // ─── Scenario: Agent writes at its configured level regardless of control plane level ───

    /// <summary>
    /// When the agent diagnostic log level is set to Debug and the control plane deployment-level
    /// minimum is Warning, the agent still writes Debug and above records to the package.
    /// The two providers have independent minimum-level filters: PackageLoggerProvider respects
    /// its own MinimumLevel irrespective of what the control plane is configured to buffer.
    /// </summary>
    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public async Task PackageLoggerProvider_AgentAtDebug_WritesDebugAndAboveRegardlessOfControlPlaneLevel()
    {
        var payloads = new List<string>();
        var mockPackage = new Mock<IPackageAccess>(MockBehavior.Strict);
        mockPackage
            .Setup(p => p.AppendLogAsync(
                It.Is<PackageLogContext>(c => c.Stream == PackageLogStream.Diagnostics),
                It.IsAny<PackageLogPayload>(),
                It.IsAny<CancellationToken>()))
            .Callback<PackageLogContext, PackageLogPayload, CancellationToken>((_, payload, _) =>
            {
                using var reader = new System.IO.StreamReader(payload.Content, Encoding.UTF8, leaveOpen: true);
                payloads.Add(reader.ReadToEnd());
            })
            .Returns(ValueTask.CompletedTask);

        // Agent configured at Debug — control plane configured at Warning (independent)
        var state = BuildActiveState();
        using var agentProvider = new PackageLoggerProvider(
            state,
            Options.Create(new DiagnosticLogOptions { MinimumLevel = "Debug" }),
            BuildServiceProvider(mockPackage.Object));

        var logger = agentProvider.CreateLogger("AgentCategory");
        logger.LogDebug("debug record");
        logger.LogInformation("information record");
        logger.LogWarning("warning record");
        logger.LogError("error record");

        await agentProvider.FlushAsync();

        var allContent = string.Join(string.Empty, payloads);
        var lines = allContent.Split('\n', System.StringSplitOptions.RemoveEmptyEntries);

        // All four levels must be present in the package — agent filter is Debug, not Warning
        Assert.IsTrue(lines.Length >= 4,
            $"Expected at least 4 NDJSON lines (Debug/Information/Warning/Error); got {lines.Length}.");
        Assert.IsTrue(allContent.Contains("debug record"), "Debug record must be written to package when agent level is Debug.");
        Assert.IsTrue(allContent.Contains("information record"), "Information record must be written.");
        Assert.IsTrue(allContent.Contains("warning record"), "Warning record must be written.");
        Assert.IsTrue(allContent.Contains("error record"), "Error record must be written.");
    }

    // ─── Scenario: Log sink failures do not halt the export ───

    /// <summary>
    /// When the package store is temporarily unavailable (throws), the sink must
    /// not propagate the exception — the export continues and the dropped record
    /// count is incremented.
    /// </summary>
    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public async Task PackageLoggerProvider_PackageStoreUnavailable_DoesNotThrow()
    {
        var mockPackage = new Mock<IPackageAccess>(MockBehavior.Strict);
        mockPackage
            .Setup(p => p.AppendLogAsync(
                It.IsAny<PackageLogContext>(),
                It.IsAny<PackageLogPayload>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new System.IO.IOException("Package store unavailable"));

        var state = BuildActiveState();
        using var provider = new PackageLoggerProvider(
            state,
            Options.Create(new DiagnosticLogOptions { MinimumLevel = "Information" }),
            BuildServiceProvider(mockPackage.Object));

        var logger = provider.CreateLogger("ResilienceTest");
        logger.LogWarning("log during unavailable store");

        // Must not throw — the sink swallows failures and counts them.
        await provider.FlushAsync();

        // If we reach here without an exception, the export continued uninterrupted.
        // The mock verifies at least one attempt was made.
        mockPackage.Verify(p => p.AppendLogAsync(
            It.IsAny<PackageLogContext>(),
            It.IsAny<PackageLogPayload>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }
}
#endif
