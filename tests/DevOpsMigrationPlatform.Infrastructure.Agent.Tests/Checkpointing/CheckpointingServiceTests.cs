// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Agent.Context;
using DevOpsMigrationPlatform.Abstractions.Agent.Export;
using DevOpsMigrationPlatform.Infrastructure.Agent.Checkpointing;
using Microsoft.Extensions.Configuration;
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
    public async Task ReadCursorAsync_WhenScopeCannotBeResolved_ThrowsInvalidOperationException()
    {
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => _sut.ReadCursorAsync("workitems", CancellationToken.None));
    }

    [TestMethod]
    public async Task WriteCursorAsync_WhenScopeCannotBeResolved_ThrowsInvalidOperationException()
    {
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => _sut.WriteCursorAsync("workitems", new CursorEntry
            {
                LastProcessed = "WorkItems/2024-01-01/00000000000001-1-1/",
                Stage = CursorStage.Completed,
                UpdatedAt = System.DateTimeOffset.UtcNow
            }, CancellationToken.None));
    }

    [TestMethod]
    public async Task DeleteCursorAsync_WhenModeProvidesAction_DeletesProjectScopedKey()
    {
        const string endpointUrl = "https://dev.azure.com/contoso";
        const string projectName = "MyProject";
        var expectedKey = PackagePaths.CursorFile("dependencies", "dependencies", endpointUrl, projectName);

        var endpointAccessor = new Mock<ICurrentJobEndpointAccessor>(MockBehavior.Strict);
        var sourceInfo = new Mock<ISourceEndpointInfo>(MockBehavior.Strict);
        sourceInfo.SetupGet(s => s.Url).Returns(endpointUrl);
        sourceInfo.SetupGet(s => s.Project).Returns(projectName);
        sourceInfo.SetupGet(s => s.ConnectorType).Returns("AzureDevOpsServices");
        endpointAccessor.SetupGet(a => a.Source).Returns(sourceInfo.Object);
        endpointAccessor.SetupGet(a => a.Target).Returns((ITargetEndpointInfo?)null);

        var packageConfigAccessor = new Mock<ICurrentPackageConfigAccessor>(MockBehavior.Strict);
        var currentConfig = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["MigrationPlatform:Mode"] = "dependencies"
            })
            .Build();
        packageConfigAccessor.SetupGet(a => a.Current).Returns(currentConfig);

        _mockStateStore
            .Setup(s => s.DeleteAsync(expectedKey, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var sut = new CheckpointingService(_mockStateStore.Object, endpointAccessor.Object, packageConfigAccessor.Object);
        await sut.DeleteCursorAsync("dependencies", CancellationToken.None);

        _mockStateStore.Verify(s => s.DeleteAsync(expectedKey, It.IsAny<CancellationToken>()), Times.Once);
        _mockStateStore.VerifyNoOtherCalls();
    }

    [TestMethod]
    public async Task DeleteContinuationTokenAsync_WhenModeProvidesAction_DeletesProjectScopedKey()
    {
        const string endpointUrl = "https://dev.azure.com/contoso";
        const string projectName = "MyProject";
        var expectedKey = PackagePaths.ContinuationFile("dependencies", "dependencies", endpointUrl, projectName);

        var endpointAccessor = new Mock<ICurrentJobEndpointAccessor>(MockBehavior.Strict);
        var sourceInfo = new Mock<ISourceEndpointInfo>(MockBehavior.Strict);
        sourceInfo.SetupGet(s => s.Url).Returns(endpointUrl);
        sourceInfo.SetupGet(s => s.Project).Returns(projectName);
        sourceInfo.SetupGet(s => s.ConnectorType).Returns("AzureDevOpsServices");
        endpointAccessor.SetupGet(a => a.Source).Returns(sourceInfo.Object);
        endpointAccessor.SetupGet(a => a.Target).Returns((ITargetEndpointInfo?)null);

        var packageConfigAccessor = new Mock<ICurrentPackageConfigAccessor>(MockBehavior.Strict);
        var currentConfig = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["MigrationPlatform:Mode"] = "dependencies"
            })
            .Build();
        packageConfigAccessor.SetupGet(a => a.Current).Returns(currentConfig);

        _mockStateStore
            .Setup(s => s.DeleteAsync(expectedKey, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var sut = new CheckpointingService(_mockStateStore.Object, endpointAccessor.Object, packageConfigAccessor.Object);
        await sut.DeleteContinuationTokenAsync("dependencies", CancellationToken.None);

        _mockStateStore.Verify(s => s.DeleteAsync(expectedKey, It.IsAny<CancellationToken>()), Times.Once);
        _mockStateStore.VerifyNoOtherCalls();
    }

    [TestMethod]
    public async Task DeleteCursorAsync_WhenOnlyPackageConfigProvidesSimulatedProjectScope_DeletesConfiguredProjectScopedKey()
    {
        const string expectedKey = "simulated/SimulatedProject/.migration/inventory.workitems.cursor.json";

        var endpointAccessor = new Mock<ICurrentJobEndpointAccessor>(MockBehavior.Strict);
        endpointAccessor.SetupGet(a => a.Source).Returns((ISourceEndpointInfo?)null);
        endpointAccessor.SetupGet(a => a.Target).Returns((ITargetEndpointInfo?)null);

        var packageConfigAccessor = new Mock<ICurrentPackageConfigAccessor>(MockBehavior.Strict);
        var currentConfig = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["MigrationPlatform:Mode"] = "inventory",
                ["MigrationPlatform:Organisations:0:Type"] = "Simulated",
                ["MigrationPlatform:Organisations:0:Projects:0"] = "SimulatedProject"
            })
            .Build();
        packageConfigAccessor.SetupGet(a => a.Current).Returns(currentConfig);

        _mockStateStore
            .Setup(s => s.DeleteAsync(expectedKey, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var sut = new CheckpointingService(_mockStateStore.Object, endpointAccessor.Object, packageConfigAccessor.Object);
        await sut.DeleteCursorAsync("WorkItems", CancellationToken.None);

        _mockStateStore.Verify(s => s.DeleteAsync(expectedKey, It.IsAny<CancellationToken>()), Times.Once);
        _mockStateStore.VerifyNoOtherCalls();
    }

    [TestMethod]
    public async Task DeleteCursorAsync_WhenOnlyTargetConfigProvidesSimulatedProjectScope_DeletesConfiguredProjectScopedKey()
    {
        const string expectedKey = "simulated/SimulatedProject/.migration/import.identities.cursor.json";

        var endpointAccessor = new Mock<ICurrentJobEndpointAccessor>(MockBehavior.Strict);
        endpointAccessor.SetupGet(a => a.Source).Returns((ISourceEndpointInfo?)null);
        endpointAccessor.SetupGet(a => a.Target).Returns((ITargetEndpointInfo?)null);

        var packageConfigAccessor = new Mock<ICurrentPackageConfigAccessor>(MockBehavior.Strict);
        var currentConfig = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["MigrationPlatform:Mode"] = "import",
                ["MigrationPlatform:Target:Type"] = "Simulated",
                ["MigrationPlatform:Target:Project"] = "SimulatedProject"
            })
            .Build();
        packageConfigAccessor.SetupGet(a => a.Current).Returns(currentConfig);

        _mockStateStore
            .Setup(s => s.DeleteAsync(expectedKey, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var sut = new CheckpointingService(_mockStateStore.Object, endpointAccessor.Object, packageConfigAccessor.Object);
        await sut.DeleteCursorAsync("Identities", CancellationToken.None);

        _mockStateStore.Verify(s => s.DeleteAsync(expectedKey, It.IsAny<CancellationToken>()), Times.Once);
        _mockStateStore.VerifyNoOtherCalls();
    }

    [TestMethod]
    public async Task DeleteContinuationTokenAsync_WhenOnlyPackageConfigProvidesSimulatedProjectScope_DeletesConfiguredProjectScopedKey()
    {
        const string expectedKey = "simulated/SimulatedProject/.migration/inventory.workitems.continuation.json";

        var endpointAccessor = new Mock<ICurrentJobEndpointAccessor>(MockBehavior.Strict);
        endpointAccessor.SetupGet(a => a.Source).Returns((ISourceEndpointInfo?)null);
        endpointAccessor.SetupGet(a => a.Target).Returns((ITargetEndpointInfo?)null);

        var packageConfigAccessor = new Mock<ICurrentPackageConfigAccessor>(MockBehavior.Strict);
        var currentConfig = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["MigrationPlatform:Mode"] = "inventory",
                ["MigrationPlatform:Organisations:0:Type"] = "Simulated",
                ["MigrationPlatform:Organisations:0:Projects:0"] = "SimulatedProject"
            })
            .Build();
        packageConfigAccessor.SetupGet(a => a.Current).Returns(currentConfig);

        _mockStateStore
            .Setup(s => s.DeleteAsync(expectedKey, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var sut = new CheckpointingService(_mockStateStore.Object, endpointAccessor.Object, packageConfigAccessor.Object);
        await sut.DeleteContinuationTokenAsync("WorkItems", CancellationToken.None);

        _mockStateStore.Verify(s => s.DeleteAsync(expectedKey, It.IsAny<CancellationToken>()), Times.Once);
        _mockStateStore.VerifyNoOtherCalls();
    }

    [TestMethod]
    public async Task DeleteCursorAsync_WhenProjectScopeCannotYetBeResolved_DoesNothing()
    {
        var endpointAccessor = new Mock<ICurrentJobEndpointAccessor>(MockBehavior.Strict);
        endpointAccessor.SetupGet(a => a.Source).Returns((ISourceEndpointInfo?)null);
        endpointAccessor.SetupGet(a => a.Target).Returns((ITargetEndpointInfo?)null);

        var packageConfigAccessor = new Mock<ICurrentPackageConfigAccessor>(MockBehavior.Strict);
        var currentConfig = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["MigrationPlatform:Mode"] = "Migrate",
                ["MigrationPlatform:Source:Type"] = "Simulated",
                ["MigrationPlatform:Source:Generator:Projects:0:Name"] = "RoundtripProject"
            })
            .Build();
        packageConfigAccessor.SetupGet(a => a.Current).Returns(currentConfig);

        var sut = new CheckpointingService(_mockStateStore.Object, endpointAccessor.Object, packageConfigAccessor.Object);
        await sut.DeleteCursorAsync("Identities", CancellationToken.None);

        _mockStateStore.VerifyNoOtherCalls();
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
    public async Task ReadCursorAsync_WhenLiveSourceHasProjectButNoUrl_UsesConnectorScopedKey()
    {
        const string projectName = "SimulatedProject";
        const string expectedKey = "simulated/SimulatedProject/.migration/export.workitems.cursor.json";

        var endpointAccessor = new Mock<ICurrentJobEndpointAccessor>(MockBehavior.Strict);
        var sourceInfo = new Mock<ISourceEndpointInfo>(MockBehavior.Strict);
        sourceInfo.SetupGet(s => s.Url).Returns(string.Empty);
        sourceInfo.SetupGet(s => s.Project).Returns(projectName);
        sourceInfo.SetupGet(s => s.ConnectorType).Returns("Simulated");
        endpointAccessor.SetupGet(a => a.Source).Returns(sourceInfo.Object);
        endpointAccessor.SetupGet(a => a.Target).Returns((ITargetEndpointInfo?)null);

        var entry = new CursorEntry
        {
            LastProcessed = "WorkItems/2024-01-01/00000000000001-1-1/",
            Stage = CursorStage.Completed,
            UpdatedAt = System.DateTimeOffset.UtcNow
        };

        _mockStateStore
            .Setup(s => s.ReadAsync(expectedKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync(JsonSerializer.Serialize(entry));

        var sut = new CheckpointingService(_mockStateStore.Object, endpointAccessor.Object);
        var result = await sut.ReadCursorAsync("export.workitems", CancellationToken.None);

        Assert.IsNotNull(result);
        Assert.AreEqual(entry.LastProcessed, result.LastProcessed);
        Assert.AreEqual(entry.Stage, result.Stage);
    }

    [TestMethod]
    public async Task ReadCursorAsync_WhenOnlySourceConfigProvidesSimulatedProjectScope_UsesConfiguredProjectScopedKey()
    {
        const string expectedKey = "simulated/SimulatedProject/.migration/export.workitems.cursor.json";

        var endpointAccessor = new Mock<ICurrentJobEndpointAccessor>(MockBehavior.Strict);
        endpointAccessor.SetupGet(a => a.Source).Returns((ISourceEndpointInfo?)null);
        endpointAccessor.SetupGet(a => a.Target).Returns((ITargetEndpointInfo?)null);

        var packageConfigAccessor = new Mock<ICurrentPackageConfigAccessor>(MockBehavior.Strict);
        var currentConfig = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["MigrationPlatform:Mode"] = "export",
                ["MigrationPlatform:Source:Type"] = "Simulated",
                ["MigrationPlatform:Source:Project"] = "SimulatedProject"
            })
            .Build();
        packageConfigAccessor.SetupGet(a => a.Current).Returns(currentConfig);

        var entry = new CursorEntry
        {
            LastProcessed = "WorkItems/2024-01-01/00000000000001-1-1/",
            Stage = CursorStage.Completed,
            UpdatedAt = System.DateTimeOffset.UtcNow
        };

        _mockStateStore
            .Setup(s => s.ReadAsync(expectedKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync(JsonSerializer.Serialize(entry));

        var sut = new CheckpointingService(_mockStateStore.Object, endpointAccessor.Object, packageConfigAccessor.Object);
        var result = await sut.ReadCursorAsync("export.workitems", CancellationToken.None);

        Assert.IsNotNull(result);
        Assert.AreEqual(entry.LastProcessed, result.LastProcessed);
        Assert.AreEqual(entry.Stage, result.Stage);
    }

    [TestMethod]
    public async Task ReadCursorAsync_WhenActionQualifiedKeyIsMissing_DoesNotProbeLegacyRootKeys()
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

        _mockStateStore
            .Setup(s => s.ReadAsync(expectedKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        var sut = new CheckpointingService(_mockStateStore.Object, endpointAccessor.Object);
        var result = await sut.ReadCursorAsync("export.workitems", CancellationToken.None);

        Assert.IsNull(result);
        _mockStateStore.Verify(s => s.ReadAsync(expectedKey, It.IsAny<CancellationToken>()), Times.Once);
        _mockStateStore.VerifyNoOtherCalls();
    }

    [TestMethod]
    public async Task ReadContinuationTokenAsync_WhenActionQualifiedKeyIsMissing_DoesNotProbeLegacyRootKeys()
    {
        const string endpointUrl = "https://dev.azure.com/contoso";
        const string projectName = "MyProject";
        var expectedKey = PackagePaths.ContinuationFile("export", "workitems", endpointUrl, projectName);

        var endpointAccessor = new Mock<ICurrentJobEndpointAccessor>(MockBehavior.Strict);
        var sourceInfo = new Mock<ISourceEndpointInfo>(MockBehavior.Strict);
        sourceInfo.SetupGet(s => s.Url).Returns(endpointUrl);
        sourceInfo.SetupGet(s => s.Project).Returns(projectName);
        sourceInfo.SetupGet(s => s.ConnectorType).Returns("AzureDevOpsServices");
        endpointAccessor.SetupGet(a => a.Source).Returns(sourceInfo.Object);
        endpointAccessor.SetupGet(a => a.Target).Returns((ITargetEndpointInfo?)null);

        _mockStateStore
            .Setup(s => s.ReadAsync(expectedKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        var sut = new CheckpointingService(_mockStateStore.Object, endpointAccessor.Object);
        var result = await sut.ReadContinuationTokenAsync("export.workitems", CancellationToken.None);

        Assert.IsNull(result);
        _mockStateStore.Verify(s => s.ReadAsync(expectedKey, It.IsAny<CancellationToken>()), Times.Once);
        _mockStateStore.VerifyNoOtherCalls();
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

    [TestMethod]
    public async Task WriteContinuationTokenAsync_WhenActionQualified_WritesProjectScopedKey()
    {
        const string endpointUrl = "https://dev.azure.com/contoso";
        const string projectName = "MyProject";
        var expectedKey = PackagePaths.ContinuationFile("import", "workitems", endpointUrl, projectName);

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
        await sut.WriteContinuationTokenAsync("import.workitems", new BatchContinuationToken
        {
            ChangedDateUtc = System.DateTime.UtcNow,
            WorkItemId = 7,
            QueryFingerprint = "fingerprint",
            GeneratedAtUtc = System.DateTime.UtcNow
        }, CancellationToken.None);

        Assert.AreEqual(expectedKey, capturedKey);
    }
}
