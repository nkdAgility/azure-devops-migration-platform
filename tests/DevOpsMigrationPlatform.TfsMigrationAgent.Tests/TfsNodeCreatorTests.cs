// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions.Agent.Tools;
using DevOpsMigrationPlatform.Infrastructure.TfsObjectModel.Nodes;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.TeamFoundation.Server;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace DevOpsMigrationPlatform.TfsMigrationAgent.Tests;

[TestClass]
public class TfsNodeCreatorTests
{
    private const string ProjectName = "TargetProject";
    private const string ProjectUri = "vstfs:///Classification/TeamProject/1";

    [TestMethod]
    public async Task EnsureExistsAsync_AreaPath_CreatesMissingHierarchy()
    {
        var css = new Mock<ICommonStructureService4>(MockBehavior.Strict);
        css.Setup(s => s.ListStructures(ProjectUri)).Returns(
        [
            CreateNodeInfo("area-root-uri", @"\TargetProject", "ProjectModelHierarchyArea")
        ]);
        css.Setup(s => s.CreateNode("Area", "area-root-uri", null, null)).Returns("area-uri");
        css.Setup(s => s.CreateNode("SubArea", "area-uri", null, null)).Returns("subarea-uri");

        var sut = new TfsNodeCreator(css.Object, NullLogger<TfsNodeCreator>.Instance, ProjectName, ProjectUri);

        await sut.EnsureExistsAsync(ClassificationNodeType.Area, @"TargetProject\Area\SubArea", CancellationToken.None);

        css.Verify(s => s.CreateNode("Area", "area-root-uri", null, null), Times.Once);
        css.Verify(s => s.CreateNode("SubArea", "area-uri", null, null), Times.Once);
    }

    [TestMethod]
    public async Task EnsureExistsAsync_IterationPath_CreatesMissingHierarchy()
    {
        var css = new Mock<ICommonStructureService4>(MockBehavior.Strict);
        css.Setup(s => s.ListStructures(ProjectUri)).Returns(
        [
            CreateNodeInfo("iter-root-uri", @"\TargetProject", "ProjectLifecycleIteration")
        ]);
        css.Setup(s => s.CreateNode("Iterations", "iter-root-uri", null, null)).Returns("iterations-uri");
        css.Setup(s => s.CreateNode("Sprint 1", "iterations-uri", null, null)).Returns("sprint-uri");

        var sut = new TfsNodeCreator(css.Object, NullLogger<TfsNodeCreator>.Instance, ProjectName, ProjectUri);

        await sut.EnsureExistsAsync(ClassificationNodeType.Iteration, @"TargetProject\Iterations\Sprint 1", CancellationToken.None);

        css.Verify(s => s.CreateNode("Iterations", "iter-root-uri", null, null), Times.Once);
        css.Verify(s => s.CreateNode("Sprint 1", "iterations-uri", null, null), Times.Once);
    }

    [TestMethod]
    public async Task SetIterationDatesAsync_MissingPath_CreatesLeafWithDates()
    {
        var start = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var finish = new DateTimeOffset(2026, 1, 14, 0, 0, 0, TimeSpan.Zero);

        var css = new Mock<ICommonStructureService4>(MockBehavior.Strict);
        css.Setup(s => s.ListStructures(ProjectUri)).Returns(
        [
            CreateNodeInfo("iter-root-uri", @"\TargetProject", "ProjectLifecycleIteration")
        ]);
        css.Setup(s => s.CreateNode("Iterations", "iter-root-uri", null, null)).Returns("iterations-uri");
        css.Setup(s => s.CreateNode("Sprint 2", "iterations-uri", start.UtcDateTime, finish.UtcDateTime)).Returns("sprint2-uri");

        var sut = new TfsNodeCreator(css.Object, NullLogger<TfsNodeCreator>.Instance, ProjectName, ProjectUri);

        await sut.SetIterationDatesAsync(@"TargetProject\Iterations\Sprint 2", start, finish, CancellationToken.None);

        css.Verify(s => s.CreateNode("Iterations", "iter-root-uri", null, null), Times.Once);
        css.Verify(s => s.CreateNode("Sprint 2", "iterations-uri", start.UtcDateTime, finish.UtcDateTime), Times.Once);
    }

    [TestMethod]
    public async Task EnsureExistsAsync_AreaPath_WithProjectModelHierarchyRoot_CreatesHierarchy()
    {
        var css = new Mock<ICommonStructureService4>(MockBehavior.Strict);
        css.Setup(s => s.ListStructures(ProjectUri)).Returns(
        [
            CreateNodeInfo("area-root-uri", @"\TargetProject", "ProjectModelHierarchy")
        ]);
        css.Setup(s => s.CreateNode("Platform", "area-root-uri", null, null)).Returns("platform-uri");
        css.Setup(s => s.CreateNode("Backend", "platform-uri", null, null)).Returns("backend-uri");

        var sut = new TfsNodeCreator(css.Object, NullLogger<TfsNodeCreator>.Instance, ProjectName, ProjectUri);

        await sut.EnsureExistsAsync(ClassificationNodeType.Area, @"TargetProject\Platform\Backend", CancellationToken.None);

        css.Verify(s => s.CreateNode("Platform", "area-root-uri", null, null), Times.Once);
        css.Verify(s => s.CreateNode("Backend", "platform-uri", null, null), Times.Once);
    }

    [TestMethod]
    public async Task EnsureExistsAsync_FullTreeAcrossCalls_CreatesBranchesUnderSharedParent()
    {
        var css = new Mock<ICommonStructureService4>(MockBehavior.Strict);
        css.SetupSequence(s => s.ListStructures(ProjectUri))
            .Returns(
            [
                CreateNodeInfo("area-root-uri", @"\TargetProject", "ProjectModelHierarchy")
            ])
            .Returns(
            [
                CreateNodeInfo("area-root-uri", @"\TargetProject", "ProjectModelHierarchy"),
                CreateNodeInfo("platform-uri", @"\TargetProject\Platform", "ProjectModelHierarchyArea")
            ]);
        css.Setup(s => s.CreateNode("Platform", "area-root-uri", null, null)).Returns("platform-uri");
        css.Setup(s => s.CreateNode("Backend", "platform-uri", null, null)).Returns("backend-uri");
        css.Setup(s => s.CreateNode("Frontend", "platform-uri", null, null)).Returns("frontend-uri");

        var sut = new TfsNodeCreator(css.Object, NullLogger<TfsNodeCreator>.Instance, ProjectName, ProjectUri);

        await sut.EnsureExistsAsync(ClassificationNodeType.Area, @"TargetProject\Platform\Backend", CancellationToken.None);
        await sut.EnsureExistsAsync(ClassificationNodeType.Area, @"TargetProject\Platform\Frontend", CancellationToken.None);

        css.Verify(s => s.CreateNode("Platform", "area-root-uri", null, null), Times.Once);
        css.Verify(s => s.CreateNode("Backend", "platform-uri", null, null), Times.Once);
        css.Verify(s => s.CreateNode("Frontend", "platform-uri", null, null), Times.Once);
    }

    [TestMethod]
    public async Task NodeExistsAsync_NormalizesTrailingPathSeparators_FromTfsMetadata()
    {
        var css = new Mock<ICommonStructureService4>(MockBehavior.Strict);
        css.Setup(s => s.ListStructures(ProjectUri)).Returns(
        [
            CreateNodeInfo("area-root-uri", @"\TargetProject", "ProjectModelHierarchy"),
            CreateNodeInfo("team-uri", @"\TargetProject\Team A\", "ProjectModelHierarchyArea")
        ]);

        var sut = new TfsNodeCreator(css.Object, NullLogger<TfsNodeCreator>.Instance, ProjectName, ProjectUri);

        var exists = await sut.NodeExistsAsync(
            ClassificationNodeType.Area,
            @"TargetProject\Team A",
            CancellationToken.None);

        Assert.IsTrue(exists);
    }

    [TestMethod]
    public async Task NodeExistsAsync_NormalizesForwardSlashPath_WithLeadingAndTrailingSeparators()
    {
        var css = new Mock<ICommonStructureService4>(MockBehavior.Strict);
        css.Setup(s => s.ListStructures(ProjectUri)).Returns(
        [
            CreateNodeInfo("area-root-uri", @"\TargetProject", "ProjectModelHierarchy"),
            CreateNodeInfo("team-uri", @"\TargetProject\Team A", "ProjectModelHierarchyArea")
        ]);

        var sut = new TfsNodeCreator(css.Object, NullLogger<TfsNodeCreator>.Instance, ProjectName, ProjectUri);

        var exists = await sut.NodeExistsAsync(
            ClassificationNodeType.Area,
            " /TargetProject/Team A/ ",
            CancellationToken.None);

        Assert.IsTrue(exists);
    }

    private static NodeInfo CreateNodeInfo(string uri, string path, string structureType)
        => new()
        {
            Uri = uri,
            Path = path,
            StructureType = structureType
        };
}
