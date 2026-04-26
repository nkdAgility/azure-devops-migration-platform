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

public class ReplicateSourceTreeContext
{
    private static readonly JsonSerializerOptions s_jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true
    };

    public bool ReplicateSourceTreeEnabled { get; set; } = true;
    public bool SourceTreeArtifactAbsent { get; set; } = false;

    private readonly List<string> _areaNodes = new();
    private readonly List<IterationNodeEntry> _iterationNodes = new();
    private readonly HashSet<string> _checkpointedPaths = new(StringComparer.OrdinalIgnoreCase);

    public bool SetIterationDatesThrows { get; set; } = false;
    public Exception? CaughtException { get; private set; }

    public Mock<INodeCreator> NodeCreatorMock { get; } = new(MockBehavior.Loose);
    public Mock<IArtefactStore> ArtefactStoreMock { get; } = new(MockBehavior.Loose);
    public Mock<IStateStore> StateStoreMock { get; } = new(MockBehavior.Loose);

    public ReplicateSourceTreeContext()
    {
        NodeCreatorMock.Setup(c => c.EnsureExistsAsync(
            It.IsAny<ClassificationNodeType>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        NodeCreatorMock.Setup(c => c.SetIterationDatesAsync(
            It.IsAny<string>(), It.IsAny<DateTimeOffset?>(), It.IsAny<DateTimeOffset?>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        StateStoreMock.Setup(s => s.WriteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
    }

    public void AddAreaNode(string path) => _areaNodes.Add(path);

    public void AddIterationNode(string path, DateTimeOffset? start, DateTimeOffset? finish)
        => _iterationNodes.Add(new IterationNodeEntry(path, start, finish, false));

    public void AddCheckpointedPath(string targetPath) => _checkpointedPaths.Add(targetPath);

    private void SetupMocks()
    {
        if (SourceTreeArtifactAbsent)
        {
            ArtefactStoreMock.Setup(s => s.ReadAsync("Nodes/source-tree.json", It.IsAny<CancellationToken>()))
                .ReturnsAsync((string?)null);
        }
        else
        {
            var snapshot = new ClassificationTreeSnapshot(_areaNodes, _iterationNodes);
            var json = JsonSerializer.Serialize(snapshot, s_jsonOptions);
            ArtefactStoreMock.Setup(s => s.ReadAsync("Nodes/source-tree.json", It.IsAny<CancellationToken>()))
                .ReturnsAsync(json);
        }

        ArtefactStoreMock.Setup(s => s.ReadAsync("Nodes/referenced-paths.json", It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        if (_checkpointedPaths.Count > 0)
        {
            var progress = new NodeReplicationProgress();
            foreach (var p in _checkpointedPaths) progress.ReplicatedPaths.Add(p);
            var progressJson = JsonSerializer.Serialize(progress, s_jsonOptions);
            StateStoreMock.Setup(s => s.ReadAsync(NodeReplicationProgress.StateKey, It.IsAny<CancellationToken>()))
                .ReturnsAsync(progressJson);
        }
        else
        {
            StateStoreMock.Setup(s => s.ReadAsync(NodeReplicationProgress.StateKey, It.IsAny<CancellationToken>()))
                .ReturnsAsync((string?)null);
        }

        if (SetIterationDatesThrows)
        {
            NodeCreatorMock.Setup(c => c.SetIterationDatesAsync(
                It.IsAny<string>(), It.IsAny<DateTimeOffset?>(), It.IsAny<DateTimeOffset?>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new Exception("Simulated date-setting failure"));
        }
    }

    public async Task RunReplicateSourceTreeAsync()
    {
        SetupMocks();

        var opts = new NodeStructureOptions
        {
            Enabled = true,
            ReplicateSourceTree = ReplicateSourceTreeEnabled,
            AreaPathMappings = [],
            IterationPathMappings = []
        };

        var tool = new NodeStructureTool(Options.Create(opts), NullLogger<NodeStructureTool>.Instance);

        var ensurer = new NodeEnsurer(
            Options.Create(opts),
            tool,
            NodeCreatorMock.Object,
            ArtefactStoreMock.Object,
            StateStoreMock.Object,
            NullLogger<NodeEnsurer>.Instance);

        var context = new ProjectMapping("SourceProject", "TargetProject");

        try
        {
            await ensurer.ReplicateSourceTreeAsync(context, CancellationToken.None);
        }
        catch (Exception ex)
        {
            CaughtException = ex;
        }
    }
}
