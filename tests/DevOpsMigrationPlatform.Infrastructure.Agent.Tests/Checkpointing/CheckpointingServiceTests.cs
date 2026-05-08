// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Agent.Context;
using DevOpsMigrationPlatform.Infrastructure.Agent.Checkpointing;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace DevOpsMigrationPlatform.Infrastructure.Tests.Checkpointing;

[TestClass]
public class CheckpointingServiceTests
{
    private Mock<IStateStore> _mockStateStore = null!;
    private CheckpointingService _sut = null!;

    [TestInitialize]
    public void Setup()
    {
        _mockStateStore = new Mock<IStateStore>(MockBehavior.Strict);
        _sut = new CheckpointingService(_mockStateStore.Object);
    }

    [TestMethod]
    public async Task ReadCursorAsync_WhenKeyIsMissing_ReturnsNull()
    {
        _mockStateStore
            .Setup(s => s.ReadAsync(PackagePaths.CursorFile("workitems"), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        var result = await _sut.ReadCursorAsync("workitems", CancellationToken.None);

        Assert.IsNull(result);
    }

    [TestMethod]
    public async Task ReadCursorAsync_WhenKeyExists_ReturnsParsedEntry()
    {
        var entry = new CursorEntry
        {
            LastProcessed = "WorkItems/2024-01-01/00000000000001-1-1/",
            Stage = CursorStage.Completed,
            UpdatedAt = System.DateTimeOffset.UtcNow
        };
        var json = JsonSerializer.Serialize(entry);

        _mockStateStore
            .Setup(s => s.ReadAsync(PackagePaths.CursorFile("workitems"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(json);

        var result = await _sut.ReadCursorAsync("workitems", CancellationToken.None);

        Assert.IsNotNull(result);
        Assert.AreEqual(entry.LastProcessed, result.LastProcessed);
        Assert.AreEqual(entry.Stage, result.Stage);
    }

    [TestMethod]
    public async Task WriteCursorAsync_WritesKeyWithLowercaseModuleName()
    {
        string? capturedKey = null;

        _mockStateStore
            .Setup(s => s.WriteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, CancellationToken>((key, _, _) => capturedKey = key)
            .Returns(Task.CompletedTask);

        var entry = new CursorEntry
        {
            LastProcessed = "WorkItems/2024-01-01/00000000000001-1-1/",
            Stage = CursorStage.Completed,
            UpdatedAt = System.DateTimeOffset.UtcNow
        };

        await _sut.WriteCursorAsync("workitems", entry, CancellationToken.None);

        Assert.AreEqual(PackagePaths.CursorFile("workitems"), capturedKey);
    }

    [TestMethod]
    public async Task WriteCursorAsync_SerialisesEntryAsJson()
    {
        string? capturedJson = null;

        _mockStateStore
            .Setup(s => s.WriteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, CancellationToken>((_, json, _) => capturedJson = json)
            .Returns(Task.CompletedTask);

        var entry = new CursorEntry
        {
            LastProcessed = "WorkItems/2024-01-01/00000000000001-1-1/",
            Stage = CursorStage.Completed,
            UpdatedAt = System.DateTimeOffset.UtcNow
        };

        await _sut.WriteCursorAsync("workitems", entry, CancellationToken.None);

        Assert.IsNotNull(capturedJson);
        var roundTripped = JsonSerializer.Deserialize<CursorEntry>(capturedJson);
        Assert.IsNotNull(roundTripped);
        Assert.AreEqual(entry.LastProcessed, roundTripped.LastProcessed);
        Assert.AreEqual(entry.Stage, roundTripped.Stage);
    }

    [TestMethod]
    public async Task ReadCursorAsync_WhenActionQualified_UsesProjectScopedKey()
    {
        const string endpointUrl = "https://dev.azure.com/contoso";
        const string projectName = "MyProject";
        var expectedKey = PackagePaths.CursorFile("export", "workitems", endpointUrl, projectName);

        var endpointAccessor = new Mock<ICurrentJobEndpointAccessor>(MockBehavior.Strict);
        var sourceInfo = new Mock<ISourceEndpointInfo>(MockBehavior.Strict);
        sourceInfo.SetupGet(s => s.Url).Returns(endpointUrl);
        sourceInfo.SetupGet(s => s.Project).Returns(projectName);
        sourceInfo.SetupGet(s => s.ConnectorType).Returns("AzureDevOpsServices");
        endpointAccessor.SetupGet(a => a.Source).Returns(sourceInfo.Object);
        endpointAccessor.SetupGet(a => a.Target).Returns((ITargetEndpointInfo?)null);

        var entry = new CursorEntry
        {
            LastProcessed = "WorkItems/2024-01-01/00000000000001-1-1/",
            Stage = CursorStage.Completed,
            UpdatedAt = System.DateTimeOffset.UtcNow
        };
        var json = JsonSerializer.Serialize(entry);

        _mockStateStore
            .Setup(s => s.ReadAsync(expectedKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync(json);

        var sut = new CheckpointingService(_mockStateStore.Object, endpointAccessor.Object);
        var result = await sut.ReadCursorAsync("export.workitems", CancellationToken.None);

        Assert.IsNotNull(result);
        Assert.AreEqual(entry.LastProcessed, result.LastProcessed);
        Assert.AreEqual(entry.Stage, result.Stage);
    }

    [TestMethod]
    public async Task WriteCursorAsync_WhenActionQualified_WritesProjectScopedKey()
    {
        const string endpointUrl = "https://dev.azure.com/contoso";
        const string projectName = "MyProject";
        var expectedKey = PackagePaths.CursorFile("import", "workitems", endpointUrl, projectName);

        var endpointAccessor = new Mock<ICurrentJobEndpointAccessor>(MockBehavior.Strict);
        var targetInfo = new Mock<ITargetEndpointInfo>(MockBehavior.Strict);
        targetInfo.SetupGet(t => t.Url).Returns(endpointUrl);
        targetInfo.SetupGet(t => t.Project).Returns(projectName);
        targetInfo.SetupGet(t => t.ConnectorType).Returns("AzureDevOpsServices");
        endpointAccessor.SetupGet(a => a.Source).Returns((ISourceEndpointInfo?)null);
        endpointAccessor.SetupGet(a => a.Target).Returns(targetInfo.Object);

        string? capturedKey = null;
        _mockStateStore
            .Setup(s => s.WriteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, CancellationToken>((key, _, _) => capturedKey = key)
            .Returns(Task.CompletedTask);

        var sut = new CheckpointingService(_mockStateStore.Object, endpointAccessor.Object);
        await sut.WriteCursorAsync("import.workitems", new CursorEntry
        {
            LastProcessed = "WorkItems/2024-01-01/00000000000001-1-1/",
            Stage = CursorStage.Completed,
            UpdatedAt = System.DateTimeOffset.UtcNow
        }, CancellationToken.None);

        Assert.AreEqual(expectedKey, capturedKey);
    }
}
