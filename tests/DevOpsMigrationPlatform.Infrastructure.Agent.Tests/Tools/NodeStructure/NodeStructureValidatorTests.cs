using DevOpsMigrationPlatform.Abstractions.Agent.Storage;
using DevOpsMigrationPlatform.Abstractions.Agent.Tools;
using DevOpsMigrationPlatform.Abstractions.Options;
using DevOpsMigrationPlatform.Infrastructure.Agent.Tools.NodeStructure;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Tests.Tools.NodeStructure;

[TestClass]
public class NodeStructureValidatorTests
{
    private static readonly ProjectMapping DefaultMapping = new("SourceProject", "TargetProject");

    private static readonly JsonSerializerOptions s_json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true
    };

    private static (NodeStructureValidator validator, Mock<IArtefactStore> storeMock)
        CreateValidator(
            NodeStructureOptions? opts = null,
            string? referencedPathsJson = null)
    {
        opts ??= new NodeStructureOptions
        {
            Enabled = true,
            AreaPathMappings = [],
            IterationPathMappings = []
        };

        var storeMock = new Mock<IArtefactStore>(MockBehavior.Loose);
        storeMock.Setup(s => s.ReadAsync("Nodes/referenced-paths.json", It.IsAny<CancellationToken>()))
            .ReturnsAsync(referencedPathsJson);

        var tool = new NodeStructureTool(Options.Create(opts), NullLogger<NodeStructureTool>.Instance);
        var validator = new NodeStructureValidator(Options.Create(opts), tool);
        return (validator, storeMock);
    }

    private static string Serialize(ReferencedPathsArtifact artifact)
        => JsonSerializer.Serialize(artifact, s_json);

    [TestMethod]
    public async Task ValidateAsync_AllPathsMapped_ReturnsValidReport()
    {
        var opts = new NodeStructureOptions
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
        var opts = new NodeStructureOptions
        {
            Enabled = true,
            AreaPathMappings = [new NodeMapping("[", "replacement")],
            IterationPathMappings = []
        };

        // Cannot create NodeStructureTool with invalid regex; use mock tool
        var storeMock = new Mock<IArtefactStore>(MockBehavior.Loose);
        storeMock.Setup(s => s.ReadAsync("Nodes/referenced-paths.json", It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        var toolMock = new Mock<INodeTranslationTool>(MockBehavior.Loose);
        toolMock.Setup(t => t.IsEnabled).Returns(true);

        var validator = new NodeStructureValidator(Options.Create(opts), toolMock.Object);

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
