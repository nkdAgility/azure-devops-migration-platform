// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) NKD Agility Limited

using DevOpsMigrationPlatform.Abstractions.Agent.Storage;
using DevOpsMigrationPlatform.Abstractions.Agent.Tools;
using DevOpsMigrationPlatform.Abstractions.Options;
using DevOpsMigrationPlatform.Infrastructure.Agent.Tools.NodeTranslation;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Tests.Tools.NodeTranslation;

[TestClass]
public class NodeTranslationValidatorTests
{
    private static readonly ProjectMapping DefaultMapping = new("SourceProject", "TargetProject");

    private static readonly JsonSerializerOptions s_json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true
    };

    private static (NodeTranslationValidator validator, Mock<IArtefactStore> storeMock)
        CreateValidator(
            NodeTranslationOptions? opts = null,
            string? referencedPathsJson = null)
    {
        opts ??= new NodeTranslationOptions
        {
            Enabled = true,
            AreaPathMappings = [],
            IterationPathMappings = []
        };

        var storeMock = new Mock<IArtefactStore>(MockBehavior.Loose);
        storeMock.Setup(s => s.ReadAsync("Nodes/referenced-paths.json", It.IsAny<CancellationToken>()))
            .ReturnsAsync(referencedPathsJson);

        var tool = new NodeTranslationTool(Options.Create(opts), NullLogger<NodeTranslationTool>.Instance);
        var validator = new NodeTranslationValidator(Options.Create(opts), tool);
        return (validator, storeMock);
    }

    private static string Serialize(ReferencedPathsArtifact artifact)
        => JsonSerializer.Serialize(artifact, s_json);

    [TestMethod]
    public async Task ValidateAsync_AllPathsMapped_ReturnsValidReport()
    {
        var opts = new NodeTranslationOptions
        {
            Enabled = true,
            AreaPathMappings = [new NodeMapping(@"^SourceProject\\(.*)", @"TargetProject\$1")],
            IterationPathMappings = []
        };
        var artifact = new ReferencedPathsArtifact(
            new[] { @"SourceProject\Team A" },
            new string[0]);

        var (validator, storeMock) = CreateValidator(opts, Serialize(artifact));

        var report = await validator.ValidateAsync(storeMock.Object, DefaultMapping, CancellationToken.None);

        Assert.IsTrue(report.IsValid);
        Assert.AreEqual(0, report.UnmappedPaths.Count);
        Assert.AreEqual(0, report.UnanchoredPaths.Count);
        Assert.AreEqual(0, report.MalformedTargetPaths.Count);
    }

    [TestMethod]
    public async Task ValidateAsync_ExternalPath_ReportsUnanchored()
    {
        var artifact = new ReferencedPathsArtifact(
            new[] { @"OtherProject\Team A" },
            new string[0]);

        var (validator, storeMock) = CreateValidator(referencedPathsJson: Serialize(artifact));

        var report = await validator.ValidateAsync(storeMock.Object, DefaultMapping, CancellationToken.None);

        Assert.IsFalse(report.IsValid);
        Assert.AreEqual(1, report.UnanchoredPaths.Count);
        Assert.AreEqual(@"OtherProject\Team A", report.UnanchoredPaths[0].Path);
    }

    [TestMethod]
    public async Task ValidateAsync_SourceProjectPath_AutoSwapped_ReturnsValid()
    {
        var artifact = new ReferencedPathsArtifact(
            new[] { @"SourceProject\Team A" },
            new string[0]);

        var (validator, storeMock) = CreateValidator(referencedPathsJson: Serialize(artifact));

        var report = await validator.ValidateAsync(storeMock.Object, DefaultMapping, CancellationToken.None);

        Assert.IsTrue(report.IsValid, "Auto-swapped path should produce a valid report.");
        Assert.AreEqual(0, report.UnanchoredPaths.Count);
        Assert.AreEqual(0, report.UnmappedPaths.Count);
    }

    [TestMethod]
    public async Task ValidateAsync_NoArtifact_ReturnsValidEmptyReport()
    {
        var (validator, storeMock) = CreateValidator(referencedPathsJson: null);

        var report = await validator.ValidateAsync(storeMock.Object, DefaultMapping, CancellationToken.None);

        Assert.IsTrue(report.IsValid);
        Assert.AreEqual(0, report.UnmappedPaths.Count);
        Assert.AreEqual(0, report.UnanchoredPaths.Count);
        Assert.AreEqual(0, report.MalformedTargetPaths.Count);
    }

    [TestMethod]
    public async Task ValidateAsync_InvalidRegexPattern_ReportsMalformed()
    {
        var opts = new NodeTranslationOptions
        {
            Enabled = true,
            AreaPathMappings = [new NodeMapping("[", "replacement")],
            IterationPathMappings = []
        };

        // Cannot create NodeTranslationTool with invalid regex; use mock tool
        var storeMock = new Mock<IArtefactStore>(MockBehavior.Loose);
        storeMock.Setup(s => s.ReadAsync("Nodes/referenced-paths.json", It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        var toolMock = new Mock<INodeTranslationTool>(MockBehavior.Loose);
        toolMock.Setup(t => t.IsEnabled).Returns(true);

        var validator = new NodeTranslationValidator(Options.Create(opts), toolMock.Object);

        var report = await validator.ValidateAsync(storeMock.Object, DefaultMapping, CancellationToken.None);

        Assert.IsFalse(report.IsValid);
        Assert.AreEqual(1, report.MalformedTargetPaths.Count);
        Assert.AreEqual("[", report.MalformedTargetPaths[0]);
    }

    [TestMethod]
    public async Task ValidateAsync_EmptyArtifact_ReturnsValidReport()
    {
        var artifact = new ReferencedPathsArtifact(new string[0], new string[0]);
        var (validator, storeMock) = CreateValidator(referencedPathsJson: Serialize(artifact));

        var report = await validator.ValidateAsync(storeMock.Object, DefaultMapping, CancellationToken.None);

        Assert.IsTrue(report.IsValid);
        Assert.AreEqual(0, report.UnmappedPaths.Count);
        Assert.AreEqual(0, report.UnanchoredPaths.Count);
    }

    [TestMethod]
    public async Task ValidateAsync_MultipleExternalPaths_AllReported()
    {
        var artifact = new ReferencedPathsArtifact(
            new[] { @"ProjectA\Node1", @"ProjectB\Node2" },
            new[] { @"ProjectC\Sprint 1" });

        var (validator, storeMock) = CreateValidator(referencedPathsJson: Serialize(artifact));

        var report = await validator.ValidateAsync(storeMock.Object, DefaultMapping, CancellationToken.None);

        Assert.IsFalse(report.IsValid);
        Assert.AreEqual(3, report.UnanchoredPaths.Count);
    }
}
