using DevOpsMigrationPlatform.Abstractions.Agent.Storage;
using DevOpsMigrationPlatform.Abstractions.Agent.Tools;
using DevOpsMigrationPlatform.Abstractions.Options;
using DevOpsMigrationPlatform.Infrastructure.Agent.Tools.NodeStructure;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Tests.Tools.NodeStructure.Steps;

public class NodeStructureValidationContext
{
    private static readonly JsonSerializerOptions s_jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true
    };

    private readonly List<string> _areaPaths = new();
    private readonly List<string> _iterationPaths = new();
    private readonly List<NodeMapping> _areaPathMappings = new();
    private readonly List<NodeMapping> _iterationPathMappings = new();
    private bool _noArtifact = false;

    public Mock<IArtefactStore> ArtefactStoreMock { get; } = new(MockBehavior.Loose);
    public NodeStructureValidationReport? ValidationReport { get; private set; }

    public void AddAreaPath(string path) => _areaPaths.Add(path);
    public void AddAreaMapping(NodeMapping mapping) => _areaPathMappings.Add(mapping);
    public void SetNoArtifact() => _noArtifact = true;

    public async Task RunValidationAsync()
    {
        if (_noArtifact || (_areaPaths.Count == 0 && _iterationPaths.Count == 0))
        {
            if (_noArtifact)
            {
                ArtefactStoreMock.Setup(s => s.ReadAsync("Nodes/referenced-paths.json", It.IsAny<CancellationToken>()))
                    .ReturnsAsync((string?)null);
            }
            else
            {
                var empty = new ReferencedPathsArtifact(new List<string>(), new List<string>());
                ArtefactStoreMock.Setup(s => s.ReadAsync("Nodes/referenced-paths.json", It.IsAny<CancellationToken>()))
                    .ReturnsAsync(JsonSerializer.Serialize(empty, s_jsonOptions));
            }
        }
        else
        {
            var artifact = new ReferencedPathsArtifact(_areaPaths, _iterationPaths);
            ArtefactStoreMock.Setup(s => s.ReadAsync("Nodes/referenced-paths.json", It.IsAny<CancellationToken>()))
                .ReturnsAsync(JsonSerializer.Serialize(artifact, s_jsonOptions));
        }

        var opts = new NodeStructureOptions
        {
            Enabled = true,
            AreaPathMappings = _areaPathMappings,
            IterationPathMappings = _iterationPathMappings
        };

        var tool = new NodeStructureTool(Options.Create(opts), NullLogger<NodeStructureTool>.Instance);
        var validator = new NodeStructureValidator(Options.Create(opts), tool);
        var context = new ProjectMapping("SourceProject", "TargetProject");

        ValidationReport = await validator.ValidateAsync(ArtefactStoreMock.Object, context, CancellationToken.None);
    }

    public async Task RunValidationWithInvalidRegexAsync(string invalidPattern)
    {
        ArtefactStoreMock.Setup(s => s.ReadAsync("Nodes/referenced-paths.json", It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        var optsWithInvalidRegex = new NodeStructureOptions
        {
            Enabled = true,
            AreaPathMappings = new List<NodeMapping> { new(invalidPattern, "replacement") },
            IterationPathMappings = new List<NodeMapping>()
        };

        // Use a mock tool since NodeStructureTool constructor rejects invalid regex.
        // The validator validates regex independently of the tool.
        var toolMock = new Mock<INodeStructureTool>(MockBehavior.Loose);
        toolMock.Setup(t => t.IsEnabled).Returns(true);

        var validator = new NodeStructureValidator(Options.Create(optsWithInvalidRegex), toolMock.Object);
        var context = new ProjectMapping("SourceProject", "TargetProject");
        ValidationReport = await validator.ValidateAsync(ArtefactStoreMock.Object, context, CancellationToken.None);
    }
}
