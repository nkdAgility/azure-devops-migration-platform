using DevOpsMigrationPlatform.Abstractions.Agent.Storage;
using DevOpsMigrationPlatform.Abstractions.Agent.Tools;
using DevOpsMigrationPlatform.Abstractions.Options;
using DevOpsMigrationPlatform.Infrastructure.Agent.Tools.NodeTranslation;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Tests.Tools.NodeTranslation.Steps;

public class AutoCreateNodesContext
{
    public bool AutoCreateNodesEnabled { get; set; } = true;
    private readonly List<string> _areaPaths = new();
    private readonly List<string> _iterationPaths = new();
    public Mock<INodeCreator> NodeCreatorMock { get; } = new(MockBehavior.Loose);
    public Mock<IArtefactStore> ArtefactStoreMock { get; } = new(MockBehavior.Loose);
    public Mock<IStateStore> StateStoreMock { get; } = new(MockBehavior.Loose);
    private NodeEnsurer? _ensurer;

    public AutoCreateNodesContext()
    {
        NodeCreatorMock.Setup(c => c.EnsureExistsAsync(
            It.IsAny<ClassificationNodeType>(), It.IsAny<string>(), It.IsAny<MigrationEndpointOptions>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        StateStoreMock.Setup(s => s.ReadAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);
        StateStoreMock.Setup(s => s.WriteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
    }

    public void AddAreaPath(string path) => _areaPaths.Add(path);
    public void AddIterationPath(string path) => _iterationPaths.Add(path);

    public void SetupArtifact()
    {
        var artifact = new ReferencedPathsArtifact(_areaPaths, _iterationPaths);
        var json = JsonSerializer.Serialize(artifact, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        ArtefactStoreMock.Setup(s => s.ReadAsync("Nodes/referenced-paths.json", It.IsAny<CancellationToken>()))
            .ReturnsAsync(json);
        ArtefactStoreMock.Setup(s => s.ReadAsync("Nodes/source-tree.json", It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);
    }

    public void SetupEmptyArtifact()
    {
        var artifact = new ReferencedPathsArtifact(new List<string>(), new List<string>());
        var json = JsonSerializer.Serialize(artifact, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        ArtefactStoreMock.Setup(s => s.ReadAsync("Nodes/referenced-paths.json", It.IsAny<CancellationToken>()))
            .ReturnsAsync(json);
        ArtefactStoreMock.Setup(s => s.ReadAsync("Nodes/source-tree.json", It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);
    }

    public NodeEnsurer BuildEnsurer()
    {
        var opts = new NodeTranslationOptions
        {
            Enabled = true,
            AutoCreateNodes = AutoCreateNodesEnabled,
            AreaPathMappings = [],
            IterationPathMappings = []
        };
        var tool = new NodeTranslationTool(Options.Create(opts), NullLogger<NodeTranslationTool>.Instance);
        _ensurer = new NodeEnsurer(
            Options.Create(opts),
            tool,
            NodeCreatorMock.Object,
            NullLogger<NodeEnsurer>.Instance);
        return _ensurer;
    }

    public NodeEnsurer GetEnsurer() => _ensurer ?? BuildEnsurer();
}
