// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions.Storage;
using DevOpsMigrationPlatform.Abstractions.Telemetry;
using DevOpsMigrationPlatform.Infrastructure.Storage.FileSystem;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Tests.Storage;

/// <summary>Captures all log calls for assertions without Moq proxy limitations.</summary>
internal sealed class CapturingLogger<T> : ILogger<T>
{
    public List<(LogLevel Level, string Message)> Entries { get; } = new();
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public bool IsEnabled(LogLevel logLevel) => true;
    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        => Entries.Add((logLevel, formatter(state, exception)));
}

[TestClass]
public class PackageMigrationConfigLoaderTests
{
    private static readonly Mock<IPlatformMetrics> _metrics = new(MockBehavior.Loose);
    private static readonly ILogger<PackageMigrationConfigLoader> _logger =
        NullLogger<PackageMigrationConfigLoader>.Instance;

    private static PackageMigrationConfigLoader CreateSut(
        Mock<IPackageAccess> package,
        ILogger<PackageMigrationConfigLoader>? logger = null,
        IPlatformMetrics? metrics = null)
        => new(
            logger ?? _logger,
            package.Object,
            metrics ?? _metrics.Object);

    // ── LoadAsync ─────────────────────────────────────────────────────────────

    [TestCategory("UnitTest")]
    [TestMethod]
    // T033: LoadAsync_WhenFileAbsent_ThrowsPackageConfigNotFoundException
    public async Task LoadAsync_WhenFileAbsent_ThrowsPackageConfigNotFoundException()
    {
        var package = new Mock<IPackageAccess>(MockBehavior.Strict);
        package.SetupSequence(p => p.RequestMetaAsync(
                It.Is<PackageMetaContext>(c => c.Kind == PackageMetaKind.MigrationConfig),
                It.IsAny<CancellationToken>()))
            .Returns(new ValueTask<PackageMetaResult>(new PackageMetaResult(".migration/migration-config.json", null)))
            .Returns(new ValueTask<PackageMetaResult>(new PackageMetaResult(".migration/migration-config.json", null)))
            .Returns(new ValueTask<PackageMetaResult>(new PackageMetaResult(".migration/migration-config.json", null)))
            .Returns(new ValueTask<PackageMetaResult>(new PackageMetaResult(".migration/migration-config.json", null)));

        var sut = CreateSut(package);

        // Act + Assert — exception must be thrown
        var ex = await Assert.ThrowsExactlyAsync<PackageConfigNotFoundException>(
            () => sut.LoadAsync(CancellationToken.None));

        // Assert — message prompts re-submission
        StringAssert.Contains(ex.Message, "Re-submit");
    }

    [TestCategory("UnitTest")]
    [TestMethod]
    public async Task LoadAsync_WhenPackageBoundaryAvailable_ReadsViaPackageBoundary()
    {
        var package = new Mock<IPackageAccess>(MockBehavior.Strict);
        package.Setup(p => p.RequestMetaAsync(
                It.Is<PackageMetaContext>(c => c.Kind == PackageMetaKind.MigrationConfig),
                It.IsAny<CancellationToken>()))
            .Returns(new ValueTask<PackageMetaResult>(new PackageMetaResult(".migration/migration-config.json", new PackageMetaPayload(new MemoryStream(Encoding.UTF8.GetBytes("""{"MigrationPlatform":{"Mode":"Export"}}"""))))));

        var sut = CreateSut(package);

        var config = await sut.LoadAsync(CancellationToken.None);

        Assert.AreEqual("Export", config["MigrationPlatform:Mode"]);
    }

    [TestCategory("UnitTest")]
    [TestMethod]
    // T033: LoadAsync_WhenFileCorrupt_ThrowsJsonException
    public async Task LoadAsync_WhenFileCorrupt_ThrowsException()
    {
        var package = new Mock<IPackageAccess>(MockBehavior.Strict);
        package.Setup(p => p.RequestMetaAsync(
                It.Is<PackageMetaContext>(c => c.Kind == PackageMetaKind.MigrationConfig),
                It.IsAny<CancellationToken>()))
            .Returns(new ValueTask<PackageMetaResult>(new PackageMetaResult(".migration/migration-config.json", new PackageMetaPayload(new MemoryStream(Encoding.UTF8.GetBytes("{ NOT VALID JSON !!!!"))))));

        var sut = CreateSut(package);

        // Act + Assert — corrupt JSON causes a parse exception (subclass of JsonException)
        var ex = await Assert.ThrowsAsync<System.Text.Json.JsonException>(
            () => sut.LoadAsync(CancellationToken.None));
        Assert.IsNotNull(ex);
    }

    [TestCategory("UnitTest")]
    [TestMethod]
    // T037: O-3 LogWarning fires when file is absent
    public async Task LoadAsync_WhenFileAbsent_LogsWarningWithMigrationConfigMessage()
    {
        var package = new Mock<IPackageAccess>(MockBehavior.Strict);
        package.SetupSequence(p => p.RequestMetaAsync(
                It.Is<PackageMetaContext>(c => c.Kind == PackageMetaKind.MigrationConfig),
                It.IsAny<CancellationToken>()))
            .Returns(new ValueTask<PackageMetaResult>(new PackageMetaResult(".migration/migration-config.json", null)))
            .Returns(new ValueTask<PackageMetaResult>(new PackageMetaResult(".migration/migration-config.json", null)))
            .Returns(new ValueTask<PackageMetaResult>(new PackageMetaResult(".migration/migration-config.json", null)))
            .Returns(new ValueTask<PackageMetaResult>(new PackageMetaResult(".migration/migration-config.json", null)));

        var capturingLogger = new CapturingLogger<PackageMigrationConfigLoader>();
        var sut = CreateSut(package, capturingLogger);

        // Act
        await Assert.ThrowsExactlyAsync<PackageConfigNotFoundException>(
            () => sut.LoadAsync(CancellationToken.None));

        // Assert: LogWarning called with message containing the config file name
        Assert.IsTrue(
            capturingLogger.Entries.Exists(e =>
                e.Level == LogLevel.Warning && e.Message.Contains("migration-config.json")),
            "Expected a LogWarning entry containing 'migration-config.json'");
    }

    [TestCategory("UnitTest")]
    [TestMethod]
    // T024: O-3 LogInformation fires at start of read
    public async Task LoadAsync_WhenSuccessful_LogsInformationAtStartAndCompletion()
    {
        var package = new Mock<IPackageAccess>(MockBehavior.Strict);
        package.Setup(p => p.RequestMetaAsync(
                It.Is<PackageMetaContext>(c => c.Kind == PackageMetaKind.MigrationConfig),
                It.IsAny<CancellationToken>()))
            .Returns(new ValueTask<PackageMetaResult>(new PackageMetaResult(".migration/migration-config.json", new PackageMetaPayload(new MemoryStream(Encoding.UTF8.GetBytes("""{"MigrationPlatform":{"Mode":"Export"}}"""))))));

        var capturingLogger = new CapturingLogger<PackageMigrationConfigLoader>();
        var sut = CreateSut(package, capturingLogger);

        // Act
        var result = await sut.LoadAsync(CancellationToken.None);

        // Assert: at least one LogInformation call
        Assert.IsTrue(
            capturingLogger.Entries.Exists(e => e.Level == LogLevel.Information),
            "Expected at least one LogInformation entry.");
        Assert.IsNotNull(result);
    }

    [TestCategory("UnitTest")]
    [TestMethod]
    // T022: O-1 ActivitySource emits "config.read" span
    public async Task LoadAsync_EmitsConfigReadActivitySpan()
    {
        var package = new Mock<IPackageAccess>(MockBehavior.Strict);
        package.Setup(p => p.RequestMetaAsync(
                It.Is<PackageMetaContext>(c => c.Kind == PackageMetaKind.MigrationConfig),
                It.IsAny<CancellationToken>()))
            .Returns(new ValueTask<PackageMetaResult>(new PackageMetaResult(".migration/migration-config.json", new PackageMetaPayload(new MemoryStream(Encoding.UTF8.GetBytes("""{"MigrationPlatform":{"Mode":"Export"}}"""))))));

        var recordedActivities = new List<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = _ => true,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = a =>
            {
                if (a.OperationName == "config.read")
                    recordedActivities.Add(a);
            }
        };
        ActivitySource.AddActivityListener(listener);

        var sut = CreateSut(package);

        // Act
        await sut.LoadAsync(CancellationToken.None);

        // Assert
        Assert.IsTrue(recordedActivities.Count > 0, "Expected at least one 'config.read' activity span.");
    }

    [TestCategory("UnitTest")]
    [TestCategory("UnitTest")]
    [TestMethod]
    // T023: ConfigReadErrors incremented on absent file
    public async Task LoadAsync_WhenFileAbsent_IncrementsConfigReadErrors()
    {
        var package = new Mock<IPackageAccess>(MockBehavior.Strict);
        package.SetupSequence(p => p.RequestMetaAsync(
                It.Is<PackageMetaContext>(c => c.Kind == PackageMetaKind.MigrationConfig),
                It.IsAny<CancellationToken>()))
            .Returns(new ValueTask<PackageMetaResult>(new PackageMetaResult(".migration/migration-config.json", null)))
            .Returns(new ValueTask<PackageMetaResult>(new PackageMetaResult(".migration/migration-config.json", null)))
            .Returns(new ValueTask<PackageMetaResult>(new PackageMetaResult(".migration/migration-config.json", null)))
            .Returns(new ValueTask<PackageMetaResult>(new PackageMetaResult(".migration/migration-config.json", null)));

        var metricsMock = new Mock<IPlatformMetrics>(MockBehavior.Loose);
        var sut = CreateSut(package, _logger, metricsMock.Object);

        // Act
        await Assert.ThrowsExactlyAsync<PackageConfigNotFoundException>(
            () => sut.LoadAsync(CancellationToken.None));

        // Assert: error counter incremented; fallback counter is not expected (no legacy path)
        metricsMock.Verify(m => m.RecordConfigReadError(It.IsAny<MetricsTagList>()), Times.Once);
        metricsMock.Verify(m => m.RecordConfigReadFallback(It.IsAny<MetricsTagList>()), Times.Never);
    }

    [TestCategory("UnitTest")]
    [TestMethod]
    // Scenario: Migration Agent retries reading config on eventual consistency delay
    public async Task LoadAsync_WhenConfigNotImmediatelyAvailable_ReturnsConfigAfterRetry()
    {
        var package = new Mock<IPackageAccess>(MockBehavior.Strict);
        package.SetupSequence(p => p.RequestMetaAsync(
                It.Is<PackageMetaContext>(c => c.Kind == PackageMetaKind.MigrationConfig),
                It.IsAny<CancellationToken>()))
            .Returns(new ValueTask<PackageMetaResult>(new PackageMetaResult(".migration/migration-config.json", null)))
            .Returns(new ValueTask<PackageMetaResult>(new PackageMetaResult(".migration/migration-config.json",
                new PackageMetaPayload(new MemoryStream(Encoding.UTF8.GetBytes("""{"MigrationPlatform":{"Mode":"Export"}}"""))))));

        var sut = CreateSut(package);

        // Act — first attempt returns null; loader retries after 100ms and succeeds on attempt 2
        var config = await sut.LoadAsync(CancellationToken.None);

        Assert.AreEqual("Export", config["MigrationPlatform:Mode"]);
    }
}




