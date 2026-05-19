// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using DevOpsMigrationPlatform.Infrastructure.Agent.Checkpointing;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Agent.Context;
using DevOpsMigrationPlatform.Abstractions.Agent.Export;
using DevOpsMigrationPlatform.Abstractions.Storage;
using DevOpsMigrationPlatform.Infrastructure.Agent.Tests.TestUtilities;
using Microsoft.Extensions.Configuration;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace DevOpsMigrationPlatform.Infrastructure.Tests.Checkpointing;

[TestClass]
public class CheckpointingServiceTests
{
    private Mock<IPackageAccess> _mockPackage = null!;
    private CheckpointingService _sut = null!;

    [TestInitialize]
    public void Setup()
    {
        _mockPackage = PackageTestFactory.CreateLooseMock();
        _sut = new CheckpointingService(package: _mockPackage.Object);
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
        var endpointAccessor = CreateSourceEndpointAccessor("https://dev.azure.com/contoso", "MyProject", "AzureDevOpsServices");
        var packageConfigAccessor = CreateConfigAccessor(new Dictionary<string, string?>
        {
            ["MigrationPlatform:Mode"] = "dependencies"
        });
        var package = new Mock<IPackageAccess>(MockBehavior.Strict);
        package
            .Setup(p => p.ResetMetaAsync(
                It.Is<PackageMetaContext>(c => c.Kind == PackageMetaKind.CheckpointCursor && c.Action == "dependencies" && string.Equals(c.Module, "dependencies", StringComparison.OrdinalIgnoreCase)),
                It.IsAny<CancellationToken>()))
            .Returns(ValueTask.CompletedTask);

        var sut = new CheckpointingService(endpointAccessor.Object, packageConfigAccessor.Object, null, package.Object);
        await sut.DeleteCursorAsync("dependencies", CancellationToken.None);

        package.VerifyAll();
    }

    [TestMethod]
    public async Task DeleteContinuationTokenAsync_WhenModeProvidesAction_DeletesProjectScopedKey()
    {
        var endpointAccessor = CreateSourceEndpointAccessor("https://dev.azure.com/contoso", "MyProject", "AzureDevOpsServices");
        var packageConfigAccessor = CreateConfigAccessor(new Dictionary<string, string?>
        {
            ["MigrationPlatform:Mode"] = "dependencies"
        });
        var package = new Mock<IPackageAccess>(MockBehavior.Strict);
        package
            .Setup(p => p.ResetMetaAsync(
                It.Is<PackageMetaContext>(c => c.Kind == PackageMetaKind.ContinuationToken && c.Action == "dependencies" && string.Equals(c.Module, "dependencies", StringComparison.OrdinalIgnoreCase)),
                It.IsAny<CancellationToken>()))
            .Returns(ValueTask.CompletedTask);

        var sut = new CheckpointingService(endpointAccessor.Object, packageConfigAccessor.Object, null, package.Object);
        await sut.DeleteContinuationTokenAsync("dependencies", CancellationToken.None);

        package.VerifyAll();
    }

    [TestMethod]
    public async Task DeleteCursorAsync_WhenOnlyPackageConfigProvidesSimulatedProjectScope_DeletesConfiguredProjectScopedKey()
    {
        var endpointAccessor = CreateEmptyEndpointAccessor();
        var packageConfigAccessor = CreateConfigAccessor(new Dictionary<string, string?>
        {
            ["MigrationPlatform:Mode"] = "inventory",
            ["MigrationPlatform:Organisations:0:Type"] = "Simulated",
            ["MigrationPlatform:Organisations:0:Projects:0"] = "SimulatedProject"
        });
        var package = new Mock<IPackageAccess>(MockBehavior.Strict);
        package
            .Setup(p => p.ResetMetaAsync(
                It.Is<PackageMetaContext>(c => c.Kind == PackageMetaKind.CheckpointCursor && c.Action == "inventory" && string.Equals(c.Module, "workitems", StringComparison.OrdinalIgnoreCase)),
                It.IsAny<CancellationToken>()))
            .Returns(ValueTask.CompletedTask);

        var sut = new CheckpointingService(endpointAccessor.Object, packageConfigAccessor.Object, null, package.Object);
        await sut.DeleteCursorAsync("WorkItems", CancellationToken.None);

        package.VerifyAll();
    }

    [TestMethod]
    public async Task DeleteCursorAsync_WhenOnlyTargetConfigProvidesSimulatedProjectScope_DeletesConfiguredProjectScopedKey()
    {
        var endpointAccessor = CreateEmptyEndpointAccessor();
        var packageConfigAccessor = CreateConfigAccessor(new Dictionary<string, string?>
        {
            ["MigrationPlatform:Mode"] = "import",
            ["MigrationPlatform:Target:Type"] = "Simulated",
            ["MigrationPlatform:Target:Project"] = "SimulatedProject"
        });
        var package = new Mock<IPackageAccess>(MockBehavior.Strict);
        package
            .Setup(p => p.ResetMetaAsync(
                It.Is<PackageMetaContext>(c => c.Kind == PackageMetaKind.CheckpointCursor && c.Action == "import" && string.Equals(c.Module, "identities", StringComparison.OrdinalIgnoreCase)),
                It.IsAny<CancellationToken>()))
            .Returns(ValueTask.CompletedTask);

        var sut = new CheckpointingService(endpointAccessor.Object, packageConfigAccessor.Object, null, package.Object);
        await sut.DeleteCursorAsync("Identities", CancellationToken.None);

        package.VerifyAll();
    }

    [TestMethod]
    public async Task DeleteContinuationTokenAsync_WhenOnlyPackageConfigProvidesSimulatedProjectScope_DeletesConfiguredProjectScopedKey()
    {
        var endpointAccessor = CreateEmptyEndpointAccessor();
        var packageConfigAccessor = CreateConfigAccessor(new Dictionary<string, string?>
        {
            ["MigrationPlatform:Mode"] = "inventory",
            ["MigrationPlatform:Organisations:0:Type"] = "Simulated",
            ["MigrationPlatform:Organisations:0:Projects:0"] = "SimulatedProject"
        });
        var package = new Mock<IPackageAccess>(MockBehavior.Strict);
        package
            .Setup(p => p.ResetMetaAsync(
                It.Is<PackageMetaContext>(c => c.Kind == PackageMetaKind.ContinuationToken && c.Action == "inventory" && string.Equals(c.Module, "workitems", StringComparison.OrdinalIgnoreCase)),
                It.IsAny<CancellationToken>()))
            .Returns(ValueTask.CompletedTask);

        var sut = new CheckpointingService(endpointAccessor.Object, packageConfigAccessor.Object, null, package.Object);
        await sut.DeleteContinuationTokenAsync("WorkItems", CancellationToken.None);

        package.VerifyAll();
    }

    [TestMethod]
    public async Task DeleteCursorAsync_WhenProjectScopeCannotYetBeResolved_DoesNothing()
    {
        var endpointAccessor = CreateEmptyEndpointAccessor();
        var packageConfigAccessor = CreateConfigAccessor(new Dictionary<string, string?>
        {
            ["MigrationPlatform:Mode"] = "Migrate",
            ["MigrationPlatform:Source:Type"] = "Simulated",
            ["MigrationPlatform:Source:Generator:Projects:0:Name"] = "RoundtripProject"
        });

        var sut = new CheckpointingService(endpointAccessor.Object, packageConfigAccessor.Object, null, _mockPackage.Object);
        await sut.DeleteCursorAsync("Identities", CancellationToken.None);

        _mockPackage.Verify(p => p.ResetMetaAsync(It.IsAny<PackageMetaContext>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task ReadCursorAsync_WhenActionQualified_UsesProjectScopedKey()
    {
        var endpointAccessor = CreateSourceEndpointAccessor("https://dev.azure.com/contoso", "MyProject", "AzureDevOpsServices");
        var entry = new CursorEntry
        {
            LastProcessed = "WorkItems/2024-01-01/00000000000001-1-1/",
            Stage = CursorStage.Completed,
            UpdatedAt = System.DateTimeOffset.UtcNow
        };
        var package = CreatePackageReturningMeta(
            PackageMetaKind.CheckpointCursor,
            "export",
            "workitems",
            JsonSerializer.Serialize(entry));

        var sut = new CheckpointingService(endpointAccessor.Object, package: package.Object);
        var result = await sut.ReadCursorAsync("export.workitems", CancellationToken.None);

        Assert.IsNotNull(result);
        Assert.AreEqual(entry.LastProcessed, result.LastProcessed);
        Assert.AreEqual(entry.Stage, result.Stage);
    }

    [TestMethod]
    public async Task ReadCursorAsync_WhenLiveSourceHasProjectButNoUrl_UsesConnectorScopedKey()
    {
        var endpointAccessor = CreateSourceEndpointAccessor(string.Empty, "SimulatedProject", "Simulated");
        var entry = new CursorEntry
        {
            LastProcessed = "WorkItems/2024-01-01/00000000000001-1-1/",
            Stage = CursorStage.Completed,
            UpdatedAt = System.DateTimeOffset.UtcNow
        };
        var package = CreatePackageReturningMeta(
            PackageMetaKind.CheckpointCursor,
            "export",
            "workitems",
            JsonSerializer.Serialize(entry));

        var sut = new CheckpointingService(endpointAccessor.Object, package: package.Object);
        var result = await sut.ReadCursorAsync("export.workitems", CancellationToken.None);

        Assert.IsNotNull(result);
        Assert.AreEqual(entry.LastProcessed, result.LastProcessed);
        Assert.AreEqual(entry.Stage, result.Stage);
    }

    [TestMethod]
    public async Task ReadCursorAsync_WhenOnlySourceConfigProvidesSimulatedProjectScope_UsesConfiguredProjectScopedKey()
    {
        var endpointAccessor = CreateEmptyEndpointAccessor();
        var packageConfigAccessor = CreateConfigAccessor(new Dictionary<string, string?>
        {
            ["MigrationPlatform:Mode"] = "export",
            ["MigrationPlatform:Source:Type"] = "Simulated",
            ["MigrationPlatform:Source:Project"] = "SimulatedProject"
        });
        var entry = new CursorEntry
        {
            LastProcessed = "WorkItems/2024-01-01/00000000000001-1-1/",
            Stage = CursorStage.Completed,
            UpdatedAt = System.DateTimeOffset.UtcNow
        };
        var package = CreatePackageReturningMeta(
            PackageMetaKind.CheckpointCursor,
            "export",
            "workitems",
            JsonSerializer.Serialize(entry));

        var sut = new CheckpointingService(endpointAccessor.Object, packageConfigAccessor.Object, null, package.Object);
        var result = await sut.ReadCursorAsync("export.workitems", CancellationToken.None);

        Assert.IsNotNull(result);
        Assert.AreEqual(entry.LastProcessed, result.LastProcessed);
        Assert.AreEqual(entry.Stage, result.Stage);
    }

    [TestMethod]
    public async Task ReadCursorAsync_WhenActionQualifiedKeyIsMissing_DoesNotProbeLegacyRootKeys()
    {
        var endpointAccessor = CreateSourceEndpointAccessor("https://dev.azure.com/contoso", "MyProject", "AzureDevOpsServices");
        var package = new Mock<IPackageAccess>(MockBehavior.Strict);
        package
            .Setup(p => p.RequestMetaAsync(
                It.Is<PackageMetaContext>(c => c.Kind == PackageMetaKind.CheckpointCursor && c.Action == "export" && string.Equals(c.Module, "workitems", StringComparison.OrdinalIgnoreCase)),
                It.IsAny<CancellationToken>()))
            .Returns(new ValueTask<PackageMetaResult>(new PackageMetaResult(PackagePathTestHelper.CursorFile("export", "workitems", "https://dev.azure.com/contoso", "MyProject"), null)));

        var sut = new CheckpointingService(endpointAccessor.Object, package: package.Object);
        var result = await sut.ReadCursorAsync("export.workitems", CancellationToken.None);

        Assert.IsNull(result);
        package.VerifyAll();
    }

    [TestMethod]
    public async Task ReadContinuationTokenAsync_WhenActionQualifiedKeyIsMissing_DoesNotProbeLegacyRootKeys()
    {
        var endpointAccessor = CreateSourceEndpointAccessor("https://dev.azure.com/contoso", "MyProject", "AzureDevOpsServices");
        var package = new Mock<IPackageAccess>(MockBehavior.Strict);
        package
            .Setup(p => p.RequestMetaAsync(
                It.Is<PackageMetaContext>(c => c.Kind == PackageMetaKind.ContinuationToken && c.Action == "export" && string.Equals(c.Module, "workitems", StringComparison.OrdinalIgnoreCase)),
                It.IsAny<CancellationToken>()))
            .Returns(new ValueTask<PackageMetaResult>(new PackageMetaResult(PackagePathTestHelper.ContinuationFile("export", "workitems", "https://dev.azure.com/contoso", "MyProject"), null)));

        var sut = new CheckpointingService(endpointAccessor.Object, package: package.Object);
        var result = await sut.ReadContinuationTokenAsync("export.workitems", CancellationToken.None);

        Assert.IsNull(result);
        package.VerifyAll();
    }

    [TestMethod]
    public async Task WriteCursorAsync_WhenActionQualified_WritesProjectScopedKey()
    {
        var endpointAccessor = CreateTargetEndpointAccessor("https://dev.azure.com/contoso", "MyProject", "AzureDevOpsServices");
        var package = new Mock<IPackageAccess>(MockBehavior.Strict);
        package
            .Setup(p => p.PersistMetaAsync(
                It.Is<PackageMetaContext>(c => c.Kind == PackageMetaKind.CheckpointCursor && c.Action == "import" && string.Equals(c.Module, "workitems", StringComparison.OrdinalIgnoreCase)),
                It.IsAny<PackageMetaPayload>(),
                It.IsAny<CancellationToken>()))
            .Returns(ValueTask.CompletedTask);

        var sut = new CheckpointingService(endpointAccessor.Object, package: package.Object);
        await sut.WriteCursorAsync("import.workitems", new CursorEntry
        {
            LastProcessed = "WorkItems/2024-01-01/00000000000001-1-1/",
            Stage = CursorStage.Completed,
            UpdatedAt = System.DateTimeOffset.UtcNow
        }, CancellationToken.None);

        package.VerifyAll();
    }

    [TestMethod]
    public async Task WriteContinuationTokenAsync_WhenActionQualified_WritesProjectScopedKey()
    {
        var endpointAccessor = CreateTargetEndpointAccessor("https://dev.azure.com/contoso", "MyProject", "AzureDevOpsServices");
        var package = new Mock<IPackageAccess>(MockBehavior.Strict);
        package
            .Setup(p => p.PersistMetaAsync(
                It.Is<PackageMetaContext>(c => c.Kind == PackageMetaKind.ContinuationToken && c.Action == "import" && string.Equals(c.Module, "workitems", StringComparison.OrdinalIgnoreCase)),
                It.IsAny<PackageMetaPayload>(),
                It.IsAny<CancellationToken>()))
            .Returns(ValueTask.CompletedTask);

        var sut = new CheckpointingService(endpointAccessor.Object, package: package.Object);
        await sut.WriteContinuationTokenAsync("import.workitems", new BatchContinuationToken
        {
            ChangedDateUtc = System.DateTime.UtcNow,
            WorkItemId = 7,
            QueryFingerprint = "fingerprint",
            GeneratedAtUtc = System.DateTime.UtcNow
        }, CancellationToken.None);

        package.VerifyAll();
    }

    [TestMethod]
    public async Task ReadCursorAsync_WhenPackageBoundaryIsAvailable_ReadsViaPackageContextPath()
    {
        var endpointAccessor = CreateSourceEndpointAccessor("https://dev.azure.com/contoso", "MyProject", "AzureDevOpsServices");
        var entry = new CursorEntry
        {
            LastProcessed = "WorkItems/2024-01-01/00000000000001-1-1/",
            Stage = CursorStage.Completed,
            UpdatedAt = System.DateTimeOffset.UtcNow
        };
        var package = CreatePackageReturningMeta(
            PackageMetaKind.CheckpointCursor,
            "export",
            "workitems",
            JsonSerializer.Serialize(entry));

        var sut = new CheckpointingService(endpointAccessor.Object, null, null, package.Object);
        var result = await sut.ReadCursorAsync("export.workitems", CancellationToken.None);

        Assert.IsNotNull(result);
        Assert.AreEqual(entry.LastProcessed, result.LastProcessed);
        Assert.AreEqual(entry.Stage, result.Stage);
        package.VerifyAll();
    }

    [TestMethod]
    public async Task WriteCursorAsync_WhenPackageBoundaryIsAvailable_WritesViaPackageContextPath()
    {
        var endpointAccessor = CreateTargetEndpointAccessor("https://dev.azure.com/contoso", "MyProject", "AzureDevOpsServices");
        var package = new Mock<IPackageAccess>(MockBehavior.Strict);
        package
            .Setup(p => p.PersistMetaAsync(
                It.Is<PackageMetaContext>(c => c.Kind == PackageMetaKind.CheckpointCursor && c.Action == "import" && string.Equals(c.Module, "workitems", StringComparison.OrdinalIgnoreCase)),
                It.IsAny<PackageMetaPayload>(),
                It.IsAny<CancellationToken>()))
            .Returns(ValueTask.CompletedTask);

        var sut = new CheckpointingService(endpointAccessor.Object, null, null, package.Object);
        await sut.WriteCursorAsync("import.workitems", new CursorEntry
        {
            LastProcessed = "WorkItems/2024-01-01/00000000000001-1-1/",
            Stage = CursorStage.Completed,
            UpdatedAt = System.DateTimeOffset.UtcNow
        }, CancellationToken.None);

        package.VerifyAll();
    }

    [TestMethod]
    public async Task ReadContinuationTokenAsync_WhenPackageBoundaryIsAvailable_ReadsViaPackageContextPath()
    {
        var endpointAccessor = CreateSourceEndpointAccessor("https://dev.azure.com/contoso", "MyProject", "AzureDevOpsServices");
        var token = new BatchContinuationToken
        {
            ChangedDateUtc = System.DateTime.UtcNow,
            WorkItemId = 7,
            QueryFingerprint = "fingerprint",
            GeneratedAtUtc = System.DateTime.UtcNow
        };
        var package = CreatePackageReturningMeta(
            PackageMetaKind.ContinuationToken,
            "export",
            "workitems",
            JsonSerializer.Serialize(token));

        var sut = new CheckpointingService(endpointAccessor.Object, null, null, package.Object);
        var result = await sut.ReadContinuationTokenAsync("export.workitems", CancellationToken.None);

        Assert.IsNotNull(result);
        Assert.AreEqual(token.WorkItemId, result.WorkItemId);
        Assert.AreEqual(token.QueryFingerprint, result.QueryFingerprint);
        package.VerifyAll();
    }

    [TestMethod]
    public async Task WriteContinuationTokenAsync_WhenPackageBoundaryIsAvailable_WritesViaPackageContextPath()
    {
        var endpointAccessor = CreateTargetEndpointAccessor("https://dev.azure.com/contoso", "MyProject", "AzureDevOpsServices");
        var package = new Mock<IPackageAccess>(MockBehavior.Strict);
        package
            .Setup(p => p.PersistMetaAsync(
                It.Is<PackageMetaContext>(c => c.Kind == PackageMetaKind.ContinuationToken && c.Action == "import" && string.Equals(c.Module, "workitems", StringComparison.OrdinalIgnoreCase)),
                It.IsAny<PackageMetaPayload>(),
                It.IsAny<CancellationToken>()))
            .Returns(ValueTask.CompletedTask);

        var sut = new CheckpointingService(endpointAccessor.Object, null, null, package.Object);
        await sut.WriteContinuationTokenAsync("import.workitems", new BatchContinuationToken
        {
            ChangedDateUtc = System.DateTime.UtcNow,
            WorkItemId = 7,
            QueryFingerprint = "fingerprint",
            GeneratedAtUtc = System.DateTime.UtcNow
        }, CancellationToken.None);

        package.VerifyAll();
    }

    private static Mock<IPackageAccess> CreatePackageReturningMeta(
        PackageMetaKind kind,
        string action,
        string module,
        string json)
    {
        var package = new Mock<IPackageAccess>(MockBehavior.Strict);
        package
            .Setup(p => p.RequestMetaAsync(
                It.Is<PackageMetaContext>(c => c.Kind == kind && c.Action == action && c.Module == module),
                It.IsAny<CancellationToken>()))
            .Returns(new ValueTask<PackageMetaResult>(
                new PackageMetaResult(
                    string.Empty,
                    new PackageMetaPayload(new MemoryStream(System.Text.Encoding.UTF8.GetBytes(json), writable: false), "application/json"))));
        return package;
    }

    private static Mock<ICurrentJobEndpointAccessor> CreateSourceEndpointAccessor(string url, string project, string connectorType)
    {
        var sourceInfo = new Mock<ISourceEndpointInfo>(MockBehavior.Strict);
        sourceInfo.SetupGet(s => s.Url).Returns(url);
        sourceInfo.SetupGet(s => s.Project).Returns(project);
        sourceInfo.SetupGet(s => s.ConnectorType).Returns(connectorType);

        var endpointAccessor = CreateEmptyEndpointAccessor();
        endpointAccessor.SetupGet(a => a.Source).Returns(sourceInfo.Object);
        return endpointAccessor;
    }

    private static Mock<ICurrentJobEndpointAccessor> CreateTargetEndpointAccessor(string url, string project, string connectorType)
    {
        var targetInfo = new Mock<ITargetEndpointInfo>(MockBehavior.Strict);
        targetInfo.SetupGet(t => t.Url).Returns(url);
        targetInfo.SetupGet(t => t.Project).Returns(project);
        targetInfo.SetupGet(t => t.ConnectorType).Returns(connectorType);

        var endpointAccessor = CreateEmptyEndpointAccessor();
        endpointAccessor.SetupGet(a => a.Target).Returns(targetInfo.Object);
        return endpointAccessor;
    }

    private static Mock<ICurrentJobEndpointAccessor> CreateEmptyEndpointAccessor()
    {
        var endpointAccessor = new Mock<ICurrentJobEndpointAccessor>(MockBehavior.Strict);
        endpointAccessor.SetupGet(a => a.Source).Returns((ISourceEndpointInfo?)null);
        endpointAccessor.SetupGet(a => a.Target).Returns((ITargetEndpointInfo?)null);
        return endpointAccessor;
    }

    private static Mock<ICurrentPackageConfigAccessor> CreateConfigAccessor(Dictionary<string, string?> values)
    {
        var accessor = new Mock<ICurrentPackageConfigAccessor>(MockBehavior.Strict);
        accessor.SetupGet(a => a.Current)
            .Returns(new ConfigurationBuilder().AddInMemoryCollection(values).Build());
        return accessor;
    }
}
