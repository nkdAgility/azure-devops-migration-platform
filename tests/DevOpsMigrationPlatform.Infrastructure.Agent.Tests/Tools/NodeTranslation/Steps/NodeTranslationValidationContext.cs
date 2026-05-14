// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using DevOpsMigrationPlatform.Abstractions.Storage;
using DevOpsMigrationPlatform.Abstractions.Agent.Tools;
using DevOpsMigrationPlatform.Abstractions.Options;
using DevOpsMigrationPlatform.Infrastructure.Agent.Tools.NodeTranslation;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Tests.Tools.NodeTranslation.Steps;

public class NodeTranslationValidationContext
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
    public NodeTranslationValidationReport? ValidationReport { get; private set; }

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

        var opts = new NodeTranslationOptions
        {
            Enabled = true,
            AreaPathMappings = _areaPathMappings,
            IterationPathMappings = _iterationPathMappings
        };

        var tool = new NodeTranslationTool(Options.Create(opts), NullLogger<NodeTranslationTool>.Instance);
        var validator = new NodeTranslationValidator(Options.Create(opts), tool);
        var context = new ProjectMapping("SourceProject", "TargetProject");

        ValidationReport = await validator.ValidateAsync(ArtefactStoreMock.Object, context, CancellationToken.None);
    }

    public async Task RunValidationWithInvalidRegexAsync(string invalidPattern)
    {
        ArtefactStoreMock.Setup(s => s.ReadAsync("Nodes/referenced-paths.json", It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        var optsWithInvalidRegex = new NodeTranslationOptions
        {
            Enabled = true,
            AreaPathMappings = new List<NodeMapping> { new(invalidPattern, "replacement") },
            IterationPathMappings = new List<NodeMapping>()
        };

        // Use a mock tool since NodeTranslationTool constructor rejects invalid regex.
        // The validator validates regex independently of the tool.
        var toolMock = new Mock<INodeTranslationTool>(MockBehavior.Loose);
        toolMock.Setup(t => t.IsEnabled).Returns(true);

        var validator = new NodeTranslationValidator(Options.Create(optsWithInvalidRegex), toolMock.Object);
        var context = new ProjectMapping("SourceProject", "TargetProject");
        ValidationReport = await validator.ValidateAsync(ArtefactStoreMock.Object, context, CancellationToken.None);
    }
}
