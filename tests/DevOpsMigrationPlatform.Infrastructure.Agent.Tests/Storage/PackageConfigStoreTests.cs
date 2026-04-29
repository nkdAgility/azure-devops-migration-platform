using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Infrastructure.Agent.Storage;
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
public class PackageConfigStoreTests
{
    private static readonly Mock<IMigrationMetrics> _metrics = new(MockBehavior.Loose);
    private static readonly ILogger<PackageConfigStore> _logger =
        NullLogger<PackageConfigStore>.Instance;

    private const string TestPackageUri = "test://package";

    /// <summary>Creates a sut wired to return <paramref name="artefactStore"/> for any URI.</summary>
    private static PackageConfigStore CreateSut(Mock<IArtefactStore> artefactStore)
    {
        var factory = new Mock<IPackageStoreFactory>(MockBehavior.Loose);
        factory.Setup(f => f.Create(It.IsAny<string>()))
            .Returns((artefactStore.Object, new Mock<IStateStore>().Object));
        return new PackageConfigStore(factory.Object, _logger, _metrics.Object);
    }

    /// <summary>Creates a sut with a custom logger, wired to return <paramref name="artefactStore"/>.</summary>
    private static PackageConfigStore CreateSut(Mock<IArtefactStore> artefactStore, ILogger<PackageConfigStore> logger, IMigrationMetrics? metrics = null)
    {
        var factory = new Mock<IPackageStoreFactory>(MockBehavior.Loose);
        factory.Setup(f => f.Create(It.IsAny<string>()))
            .Returns((artefactStore.Object, new Mock<IStateStore>().Object));
        return new PackageConfigStore(factory.Object, logger, metrics ?? _metrics.Object);
    }

    private static string CreateTempConfigFile()
    {
        var path = Path.GetTempFileName();
        File.WriteAllText(path, """{"MigrationPlatform":{"Mode":"Export"}}""");
        return path;
    }

    // ── WriteAsync ────────────────────────────────────────────────────────────

    [TestMethod]
    // T034: WriteAsync rejects when file already exists
    public async Task WriteAsync_WhenFileAlreadyExists_ThrowsInvalidOperationException()
    {
        // Arrange: store that reports ExistsAsync = true
        var store = new Mock<IArtefactStore>(MockBehavior.Loose);
        store.Setup(s => s.ExistsAsync(
                It.Is<string>(p => p.EndsWith("migration-config.json", StringComparison.OrdinalIgnoreCase)),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var sut = CreateSut(store);
        var configFile = CreateTempConfigFile();
        try
        {
            // Act + Assert
            await Assert.ThrowsExactlyAsync<InvalidOperationException>(
                () => sut.WriteAsync(TestPackageUri, configFile, false, CancellationToken.None));
        }
        finally { File.Delete(configFile); }
    }

    [TestMethod]
    // WriteAsync with force=true overwrites without throwing
    public async Task WriteAsync_WhenFileAlreadyExistsAndForceTrue_OverwritesSuccessfully()
    {
        // Arrange: store that reports ExistsAsync = true
        var store = new Mock<IArtefactStore>(MockBehavior.Loose);
        store.Setup(s => s.ExistsAsync(
                It.Is<string>(p => p.EndsWith("migration-config.json", StringComparison.OrdinalIgnoreCase)),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        store.Setup(s => s.WriteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var sut = CreateSut(store);
        var configFile = CreateTempConfigFile();
        try
        {
            // Act — should not throw
            await sut.WriteAsync(TestPackageUri, configFile, true, CancellationToken.None);
        }
        finally { File.Delete(configFile); }

        // Assert — WriteAsync was called on the artefact store
        store.Verify(s => s.WriteAsync(
            It.Is<string>(p => p.EndsWith("migration-config.json", StringComparison.OrdinalIgnoreCase)),
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    // T023: ConfigWriteCount incremented on successful write
    public async Task WriteAsync_WhenSuccessful_IncrementsConfigWriteCount()
    {
        // Arrange
        var store = new Mock<IArtefactStore>(MockBehavior.Loose);
        store.Setup(s => s.ExistsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        store.Setup(s => s.WriteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var metricsMock = new Mock<IMigrationMetrics>(MockBehavior.Loose);
        var sut = CreateSut(store, _logger, metricsMock.Object);
        var configFile = CreateTempConfigFile();
        try
        {
            // Act
            await sut.WriteAsync(TestPackageUri, configFile, false, CancellationToken.None);
        }
        finally { File.Delete(configFile); }

        // Assert: write-count metric incremented
        metricsMock.Verify(m => m.RecordConfigWriteCompleted(in It.Ref<System.Diagnostics.TagList>.IsAny), Times.Once);
    }

    [TestMethod]
    // T023: ConfigWriteError incremented when write throws
    public async Task WriteAsync_WhenWriteThrows_IncrementsConfigWriteError()
    {
        // Arrange
        var store = new Mock<IArtefactStore>(MockBehavior.Loose);
        store.Setup(s => s.ExistsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        store.Setup(s => s.WriteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new IOException("disk full"));

        var metricsMock = new Mock<IMigrationMetrics>(MockBehavior.Loose);
        var sut = CreateSut(store, _logger, metricsMock.Object);
        var configFile = CreateTempConfigFile();
        try
        {
            // Act
            await Assert.ThrowsExactlyAsync<IOException>(
                () => sut.WriteAsync(TestPackageUri, configFile, false, CancellationToken.None));
        }
        finally { File.Delete(configFile); }

        // Assert
        metricsMock.Verify(m => m.RecordConfigWriteError(in It.Ref<System.Diagnostics.TagList>.IsAny), Times.Once);
    }

    // ── ReadAsync ─────────────────────────────────────────────────────────────

    [TestMethod]
    // T033: ReadAsync_WhenFileAbsent_ThrowsPackageConfigNotFoundException
    public async Task ReadAsync_WhenFileAbsent_ThrowsPackageConfigNotFoundException()
    {
        // Arrange: all ExistsAsync calls return false (retry loop exhausted)
        var store = new Mock<IArtefactStore>(MockBehavior.Loose);
        store.Setup(s => s.ExistsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var sut = CreateSut();

        // Act + Assert — exception must be thrown
        var ex = await Assert.ThrowsExactlyAsync<PackageConfigNotFoundException>(
            () => sut.ReadAsync(store.Object, CancellationToken.None));

        // Assert — message prompts re-submission
        StringAssert.Contains(ex.Message, "Re-submit");
    }

    [TestMethod]
    // T033: ReadAsync_WhenFileCorrupt_ThrowsJsonException
    public async Task ReadAsync_WhenFileCorrupt_ThrowsException()
    {
        // Arrange: file exists but contains invalid JSON
        var store = new Mock<IArtefactStore>(MockBehavior.Loose);
        store.Setup(s => s.ExistsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        store.Setup(s => s.ReadAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("{ NOT VALID JSON !!!!");

        var sut = CreateSut();

        // Act + Assert — corrupt JSON causes a parse exception (subclass of JsonException)
        var ex = await Assert.ThrowsAsync<System.Text.Json.JsonException>(
            () => sut.ReadAsync(store.Object, CancellationToken.None));
        Assert.IsNotNull(ex);
    }

    [TestMethod]
    // T037: O-3 LogWarning fires when file is absent
    public async Task ReadAsync_WhenFileAbsent_LogsWarningWithMigrationConfigMessage()
    {
        // Arrange
        var store = new Mock<IArtefactStore>(MockBehavior.Loose);
        store.Setup(s => s.ExistsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var capturingLogger = new CapturingLogger<PackageConfigStore>();
        var sut = CreateSut(store, capturingLogger);

        // Act
        await Assert.ThrowsExactlyAsync<PackageConfigNotFoundException>(
            () => sut.ReadAsync(store.Object, CancellationToken.None));

        // Assert: LogWarning called with message containing the config file name
        Assert.IsTrue(
            capturingLogger.Entries.Exists(e =>
                e.Level == LogLevel.Warning && e.Message.Contains("migration-config.json")),
            "Expected a LogWarning entry containing 'migration-config.json'");
    }

    [TestMethod]
    // T024: O-3 LogInformation fires at start of read
    public async Task ReadAsync_WhenSuccessful_LogsInformationAtStartAndCompletion()
    {
        // Arrange: valid JSON with MigrationPlatform wrapper
        var json = """{"MigrationPlatform":{"Mode":"Export"}}""";
        var store = new Mock<IArtefactStore>(MockBehavior.Loose);
        store.Setup(s => s.ExistsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        store.Setup(s => s.ReadAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(json);

        var capturingLogger = new CapturingLogger<PackageConfigStore>();
        var sut = CreateSut(store, capturingLogger);

        // Act
        var result = await sut.ReadAsync(store.Object, CancellationToken.None);

        // Assert: at least one LogInformation call
        Assert.IsTrue(
            capturingLogger.Entries.Exists(e => e.Level == LogLevel.Information),
            "Expected at least one LogInformation entry.");
        Assert.IsNotNull(result);
    }

    [TestMethod]
    // T024: O-3 LogInformation fires at start and completion of write
    public async Task WriteAsync_WhenSuccessful_LogsInformationAtStartAndCompletion()
    {
        // Arrange
        var store = new Mock<IArtefactStore>(MockBehavior.Loose);
        store.Setup(s => s.ExistsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        store.Setup(s => s.WriteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var capturingLogger = new CapturingLogger<PackageConfigStore>();
        var sut = CreateSut(store, capturingLogger);

        var configFile = CreateTempConfigFile();
        try
        {
            // Act
            await sut.WriteAsync(TestPackageUri, configFile, false, CancellationToken.None);
        }
        finally { File.Delete(configFile); }

        // Assert: at least one LogInformation call
        Assert.IsTrue(
            capturingLogger.Entries.Exists(e => e.Level == LogLevel.Information),
            "Expected at least one LogInformation entry.");
    }

    [TestMethod]
    // T022: O-1 ActivitySource emits "config.write" span
    public async Task WriteAsync_EmitsConfigWriteActivitySpan()
    {
        // Arrange
        var store = new Mock<IArtefactStore>(MockBehavior.Loose);
        store.Setup(s => s.ExistsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        store.Setup(s => s.WriteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var recordedActivities = new List<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = _ => true,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = a =>
            {
                if (a.OperationName == "config.write")
                    recordedActivities.Add(a);
            }
        };
        ActivitySource.AddActivityListener(listener);

        var sut = CreateSut(store);

        var configFile = CreateTempConfigFile();
        try
        {
            // Act
            await sut.WriteAsync(TestPackageUri, configFile, false, CancellationToken.None);
        }
        finally { File.Delete(configFile); }

        // Assert
        Assert.IsTrue(recordedActivities.Count > 0, "Expected at least one 'config.write' activity span.");
    }

    [TestMethod]
    // T022: O-1 ActivitySource emits "config.read" span
    public async Task ReadAsync_EmitsConfigReadActivitySpan()
    {
        // Arrange
        var json = """{"MigrationPlatform":{"Mode":"Export"}}""";
        var store = new Mock<IArtefactStore>(MockBehavior.Loose);
        store.Setup(s => s.ExistsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        store.Setup(s => s.ReadAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(json);

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

        var sut = CreateSut();

        // Act
        await sut.ReadAsync(store.Object, CancellationToken.None);

        // Assert
        Assert.IsTrue(recordedActivities.Count > 0, "Expected at least one 'config.read' activity span.");
    }

    [TestMethod]
    // T023: ConfigReadErrors incremented on absent file
    public async Task ReadAsync_WhenFileAbsent_IncrementsConfigReadErrors()
    {
        // Arrange
        var store = new Mock<IArtefactStore>(MockBehavior.Loose);
        store.Setup(s => s.ExistsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var metricsMock = new Mock<IMigrationMetrics>(MockBehavior.Loose);
        var sut = CreateSut(store, _logger, metricsMock.Object);

        // Act
        await Assert.ThrowsExactlyAsync<PackageConfigNotFoundException>(
            () => sut.ReadAsync(store.Object, CancellationToken.None));

        // Assert: both error and fallback counters incremented
        metricsMock.Verify(m => m.RecordConfigReadError(in It.Ref<System.Diagnostics.TagList>.IsAny), Times.Once);
        metricsMock.Verify(m => m.RecordConfigReadFallback(in It.Ref<System.Diagnostics.TagList>.IsAny), Times.Once);
    }
}
