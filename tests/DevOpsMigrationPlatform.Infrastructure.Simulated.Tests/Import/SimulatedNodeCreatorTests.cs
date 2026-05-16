// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions.Agent.Context;
using DevOpsMigrationPlatform.Abstractions.Agent.Tools;
using DevOpsMigrationPlatform.Infrastructure.Simulated.Import;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DevOpsMigrationPlatform.Infrastructure.Simulated.Tests.Import;

[TestClass]
public sealed class SimulatedNodeCreatorTests
{
    [TestMethod]
    public async Task EnsureExistsAsync_AreaPath_AddsHierarchyToInMemoryStructure()
    {
        var sut = CreateSut(project: "TargetProject");

        await sut.EnsureExistsAsync(ClassificationNodeType.Area, @"TargetProject\Platform\Backend", CancellationToken.None);

        Assert.IsTrue(await sut.NodeExistsAsync(ClassificationNodeType.Area, "TargetProject", CancellationToken.None));
        Assert.IsTrue(await sut.NodeExistsAsync(ClassificationNodeType.Area, @"TargetProject\Platform", CancellationToken.None));
        Assert.IsTrue(await sut.NodeExistsAsync(ClassificationNodeType.Area, @"TargetProject\Platform\Backend", CancellationToken.None));
    }

    [TestMethod]
    public async Task EnsureExistsAsync_IterationPath_IsStoredSeparatelyFromAreaPaths()
    {
        var sut = CreateSut(project: "TargetProject");

        await sut.EnsureExistsAsync(ClassificationNodeType.Iteration, @"TargetProject\Sprint 1", CancellationToken.None);

        Assert.IsTrue(await sut.NodeExistsAsync(ClassificationNodeType.Iteration, @"TargetProject\Sprint 1", CancellationToken.None));
        Assert.IsFalse(await sut.NodeExistsAsync(ClassificationNodeType.Area, @"TargetProject\Sprint 1", CancellationToken.None));
    }

    [TestMethod]
    public async Task EnsureExistsAsync_PathWithoutProjectPrefix_IsNormalizedToProjectHierarchy()
    {
        var sut = CreateSut(project: "TargetProject");

        await sut.EnsureExistsAsync(ClassificationNodeType.Area, @"Platform\Mobile", CancellationToken.None);

        Assert.IsTrue(await sut.NodeExistsAsync(ClassificationNodeType.Area, @"TargetProject\Platform", CancellationToken.None));
        Assert.IsTrue(await sut.NodeExistsAsync(ClassificationNodeType.Area, @"TargetProject\Platform\Mobile", CancellationToken.None));
    }

    [TestMethod]
    public async Task NodeExistsAsync_TrailingAndForwardSlashPathVariants_AreNormalizedAsEquivalent()
    {
        var sut = CreateSut(project: "TargetProject");

        await sut.EnsureExistsAsync(ClassificationNodeType.Area, @"TargetProject\Platform\Backend", CancellationToken.None);

        Assert.IsTrue(await sut.NodeExistsAsync(ClassificationNodeType.Area, "TargetProject/Platform/Backend/", CancellationToken.None));
        Assert.IsTrue(await sut.NodeExistsAsync(ClassificationNodeType.Area, @"\TargetProject\Platform\Backend\", CancellationToken.None));
        Assert.IsTrue(await sut.NodeExistsAsync(ClassificationNodeType.Area, "Platform/Backend/", CancellationToken.None));
    }

    private static SimulatedNodeCreator CreateSut(string project)
        => new(
            NullLogger<SimulatedNodeCreator>.Instance,
            new TestTargetEndpointInfo
            {
                Url = "https://example.dev.azure.com/target",
                Project = project,
                ConnectorType = "Simulated"
            });

    private sealed record TestTargetEndpointInfo : ITargetEndpointInfo
    {
        public required string Url { get; init; }
        public required string Project { get; init; }
        public required string ConnectorType { get; init; }
    }
}
