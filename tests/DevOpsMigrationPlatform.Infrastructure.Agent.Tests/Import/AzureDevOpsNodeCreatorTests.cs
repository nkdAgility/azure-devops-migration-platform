// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions.Agent.Context;
using DevOpsMigrationPlatform.Abstractions.Agent.Tools;
using DevOpsMigrationPlatform.Abstractions.Organisations;
using DevOpsMigrationPlatform.Infrastructure.AzureDevOps;
using DevOpsMigrationPlatform.Infrastructure.AzureDevOps.Nodes;
using DevOpsMigrationPlatform.Infrastructure.AzureDevOps.Platform.AzureDevOpsAccess;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.TeamFoundation.WorkItemTracking.WebApi;
using Microsoft.TeamFoundation.WorkItemTracking.WebApi.Models;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.VisualStudio.Services.Common;
using Moq;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Tests.Import;

[TestClass]
public class AzureDevOpsNodeCreatorTests
{
    [TestMethod]
    public async Task NodeExistsAsync_WhenClassificationNodeExists_ReturnsTrue()
    {
        var factory = new Mock<IAzureDevOpsClientFactory>(MockBehavior.Strict);
        var witClient = new Mock<WorkItemTrackingHttpClient>(MockBehavior.Strict, new object[] { new Uri("https://dev.azure.com/test-org"), null! });

        factory
            .Setup(f => f.CreateWorkItemClientAsync(It.IsAny<OrganisationEndpoint>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(witClient.Object);

        witClient
            .Setup(c => c.GetClassificationNodeAsync(
                "TargetProject",
                TreeStructureGroup.Areas,
                "Platform",
                0,
                It.IsAny<object>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WorkItemClassificationNode { Name = "Platform" });

        var sut = new AzureDevOpsNodeCreator(
            factory.Object,
            NullLogger<AzureDevOpsNodeCreator>.Instance,
            new TestTargetEndpointInfo
            {
                Url = "https://dev.azure.com/test-org",
                Project = "TargetProject",
                ConnectorType = "AzureDevOpsServices"
            });

        var exists = await sut.NodeExistsAsync(ClassificationNodeType.Area, @"TargetProject\Platform", CancellationToken.None);

        Assert.IsTrue(exists);
        witClient.Verify(
            c => c.GetClassificationNodeAsync(
                "TargetProject",
                TreeStructureGroup.Areas,
                "Platform",
                0,
                It.IsAny<object>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [TestMethod]
    public async Task NodeExistsAsync_WhenClassificationNodeDoesNotExist_ReturnsFalse()
    {
        var factory = new Mock<IAzureDevOpsClientFactory>(MockBehavior.Strict);
        var witClient = new Mock<WorkItemTrackingHttpClient>(MockBehavior.Strict, new object[] { new Uri("https://dev.azure.com/test-org"), null! });

        factory
            .Setup(f => f.CreateWorkItemClientAsync(It.IsAny<OrganisationEndpoint>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(witClient.Object);

        witClient
            .Setup(c => c.GetClassificationNodeAsync(
                "TargetProject",
                TreeStructureGroup.Iterations,
                @"Sprint 1",
                0,
                It.IsAny<object>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new VssServiceException("TF401232: Classification node does not exist."));

        var sut = new AzureDevOpsNodeCreator(
            factory.Object,
            NullLogger<AzureDevOpsNodeCreator>.Instance,
            new TestTargetEndpointInfo
            {
                Url = "https://dev.azure.com/test-org",
                Project = "TargetProject",
                ConnectorType = "AzureDevOpsServices"
            });

        var exists = await sut.NodeExistsAsync(ClassificationNodeType.Iteration, @"TargetProject\Sprint 1", CancellationToken.None);

        Assert.IsFalse(exists);
        witClient.Verify(
            c => c.GetClassificationNodeAsync(
                "TargetProject",
                TreeStructureGroup.Iterations,
                @"Sprint 1",
                0,
                It.IsAny<object>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [TestMethod]
    public async Task EnsureExistsAsync_AreaPath_CreatesMissingHierarchyWithClassificationNodesApi()
    {
        var factory = new Mock<IAzureDevOpsClientFactory>(MockBehavior.Strict);
        var witClient = new Mock<WorkItemTrackingHttpClient>(MockBehavior.Strict, new object[] { new Uri("https://dev.azure.com/test-org"), null! });
        var created = new List<(TreeStructureGroup Group, string? ParentPath, string? Name)>();

        factory
            .Setup(f => f.CreateWorkItemClientAsync(It.IsAny<OrganisationEndpoint>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(witClient.Object);

        witClient
            .Setup(c => c.GetClassificationNodeAsync(
                "TargetProject",
                TreeStructureGroup.Areas,
                It.IsAny<string>(),
                0,
                It.IsAny<object>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new VssServiceException("TF401232: Classification node does not exist."));

        witClient
            .Setup(c => c.CreateOrUpdateClassificationNodeAsync(
                It.IsAny<WorkItemClassificationNode>(),
                "TargetProject",
                TreeStructureGroup.Areas,
                It.IsAny<string?>(),
                It.IsAny<object>(),
                It.IsAny<CancellationToken>()))
            .Callback<WorkItemClassificationNode, string, TreeStructureGroup, string?, object, CancellationToken>(
                (node, _, group, parentPath, _, _) => created.Add((group, parentPath, node.Name)))
            .ReturnsAsync(new WorkItemClassificationNode());

        var sut = new AzureDevOpsNodeCreator(
            factory.Object,
            NullLogger<AzureDevOpsNodeCreator>.Instance,
            new TestTargetEndpointInfo
            {
                Url = "https://dev.azure.com/test-org",
                Project = "TargetProject",
                ConnectorType = "AzureDevOpsServices"
            });

        await sut.EnsureExistsAsync(ClassificationNodeType.Area, @"TargetProject\Platform\Backend", CancellationToken.None);

        factory.Verify(
            f => f.CreateWorkItemClientAsync(It.IsAny<OrganisationEndpoint>(), It.IsAny<CancellationToken>()),
            Times.Once);
        Assert.AreEqual(2, created.Count);
        Assert.AreEqual((TreeStructureGroup.Areas, (string?)null, "Platform"), created[0]);
        Assert.AreEqual((TreeStructureGroup.Areas, @"Platform", "Backend"), created[1]);
    }

    [TestMethod]
    public async Task EnsureExistsAsync_IterationPath_CreatesMissingHierarchyWithClassificationNodesApi()
    {
        var factory = new Mock<IAzureDevOpsClientFactory>(MockBehavior.Strict);
        var witClient = new Mock<WorkItemTrackingHttpClient>(MockBehavior.Strict, new object[] { new Uri("https://dev.azure.com/test-org"), null! });
        var created = new List<(TreeStructureGroup Group, string? ParentPath, string? Name)>();

        factory
            .Setup(f => f.CreateWorkItemClientAsync(It.IsAny<OrganisationEndpoint>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(witClient.Object);

        witClient
            .Setup(c => c.GetClassificationNodeAsync(
                "TargetProject",
                TreeStructureGroup.Iterations,
                It.IsAny<string>(),
                0,
                It.IsAny<object>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new VssServiceException("TF401232: Classification node does not exist."));

        witClient
            .Setup(c => c.CreateOrUpdateClassificationNodeAsync(
                It.IsAny<WorkItemClassificationNode>(),
                "TargetProject",
                TreeStructureGroup.Iterations,
                It.IsAny<string?>(),
                It.IsAny<object>(),
                It.IsAny<CancellationToken>()))
            .Callback<WorkItemClassificationNode, string, TreeStructureGroup, string?, object, CancellationToken>(
                (node, _, group, parentPath, _, _) => created.Add((group, parentPath, node.Name)))
            .ReturnsAsync(new WorkItemClassificationNode());

        var sut = new AzureDevOpsNodeCreator(
            factory.Object,
            NullLogger<AzureDevOpsNodeCreator>.Instance,
            new TestTargetEndpointInfo
            {
                Url = "https://dev.azure.com/test-org",
                Project = "TargetProject",
                ConnectorType = "AzureDevOpsServices"
            });

        await sut.EnsureExistsAsync(ClassificationNodeType.Iteration, @"TargetProject\Program Increment\Sprint 1", CancellationToken.None);

        factory.Verify(
            f => f.CreateWorkItemClientAsync(It.IsAny<OrganisationEndpoint>(), It.IsAny<CancellationToken>()),
            Times.Once);
        Assert.AreEqual(2, created.Count);
        Assert.AreEqual((TreeStructureGroup.Iterations, (string?)null, "Program Increment"), created[0]);
        Assert.AreEqual((TreeStructureGroup.Iterations, @"Program Increment", "Sprint 1"), created[1]);
    }

    [TestMethod]
    public async Task EnsureExistsAsync_SlashSeparatedPath_CreatesFullHierarchyWithCorrectParents()
    {
        var factory = new Mock<IAzureDevOpsClientFactory>(MockBehavior.Strict);
        var witClient = new Mock<WorkItemTrackingHttpClient>(MockBehavior.Strict, new object[] { new Uri("https://dev.azure.com/test-org"), null! });
        var created = new List<(TreeStructureGroup Group, string? ParentPath, string? Name)>();

        factory
            .Setup(f => f.CreateWorkItemClientAsync(It.IsAny<OrganisationEndpoint>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(witClient.Object);

        witClient
            .Setup(c => c.GetClassificationNodeAsync(
                "TargetProject",
                TreeStructureGroup.Areas,
                It.IsAny<string>(),
                0,
                It.IsAny<object>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new VssServiceException("TF401232: Classification node does not exist."));

        witClient
            .Setup(c => c.CreateOrUpdateClassificationNodeAsync(
                It.IsAny<WorkItemClassificationNode>(),
                "TargetProject",
                TreeStructureGroup.Areas,
                It.IsAny<string?>(),
                It.IsAny<object>(),
                It.IsAny<CancellationToken>()))
            .Callback<WorkItemClassificationNode, string, TreeStructureGroup, string?, object, CancellationToken>(
                (node, _, group, parentPath, _, _) => created.Add((group, parentPath, node.Name)))
            .ReturnsAsync(new WorkItemClassificationNode());

        var sut = new AzureDevOpsNodeCreator(
            factory.Object,
            NullLogger<AzureDevOpsNodeCreator>.Instance,
            new TestTargetEndpointInfo
            {
                Url = "https://dev.azure.com/test-org",
                Project = "TargetProject",
                ConnectorType = "AzureDevOpsServices"
            });

        await sut.EnsureExistsAsync(ClassificationNodeType.Area, "TargetProject/Platform/API/Web", CancellationToken.None);

        Assert.AreEqual(3, created.Count);
        Assert.AreEqual((TreeStructureGroup.Areas, (string?)null, "Platform"), created[0]);
        Assert.AreEqual((TreeStructureGroup.Areas, @"Platform", "API"), created[1]);
        Assert.AreEqual((TreeStructureGroup.Areas, @"Platform\API", "Web"), created[2]);
    }

    [TestMethod]
    public async Task EnsureExistsAsync_PathWithTrailingSeparator_DoesNotCreateEmptyLeafNode()
    {
        var factory = new Mock<IAzureDevOpsClientFactory>(MockBehavior.Strict);
        var witClient = new Mock<WorkItemTrackingHttpClient>(MockBehavior.Strict, new object[] { new Uri("https://dev.azure.com/test-org"), null! });
        var created = new List<(TreeStructureGroup Group, string? ParentPath, string? Name)>();

        factory
            .Setup(f => f.CreateWorkItemClientAsync(It.IsAny<OrganisationEndpoint>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(witClient.Object);

        witClient
            .Setup(c => c.GetClassificationNodeAsync(
                "TargetProject",
                TreeStructureGroup.Areas,
                It.IsAny<string>(),
                0,
                It.IsAny<object>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new VssServiceException("TF401232: Classification node does not exist."));

        witClient
            .Setup(c => c.CreateOrUpdateClassificationNodeAsync(
                It.IsAny<WorkItemClassificationNode>(),
                "TargetProject",
                TreeStructureGroup.Areas,
                It.IsAny<string?>(),
                It.IsAny<object>(),
                It.IsAny<CancellationToken>()))
            .Callback<WorkItemClassificationNode, string, TreeStructureGroup, string?, object, CancellationToken>(
                (node, _, group, parentPath, _, _) => created.Add((group, parentPath, node.Name)))
            .ReturnsAsync(new WorkItemClassificationNode());

        var sut = new AzureDevOpsNodeCreator(
            factory.Object,
            NullLogger<AzureDevOpsNodeCreator>.Instance,
            new TestTargetEndpointInfo
            {
                Url = "https://dev.azure.com/test-org",
                Project = "TargetProject",
                ConnectorType = "AzureDevOpsServices"
            });

        await sut.EnsureExistsAsync(ClassificationNodeType.Area, @"TargetProject\Platform\Backend\", CancellationToken.None);

        Assert.AreEqual(2, created.Count);
        Assert.AreEqual((TreeStructureGroup.Areas, (string?)null, "Platform"), created[0]);
        Assert.AreEqual((TreeStructureGroup.Areas, @"Platform", "Backend"), created[1]);
    }

    private sealed class TestTargetEndpointInfo : ITargetEndpointInfo
    {
        public required string Url { get; init; }
        public required string Project { get; init; }
        public required string ConnectorType { get; init; }

        public OrganisationEndpoint ToOrganisationEndpoint() => new()
        {
            ResolvedUrl = Url,
            Type = ConnectorType
        };
    }
}
