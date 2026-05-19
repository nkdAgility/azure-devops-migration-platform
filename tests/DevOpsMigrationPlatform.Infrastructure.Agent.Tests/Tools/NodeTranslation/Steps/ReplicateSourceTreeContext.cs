// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited.

using DevOpsMigrationPlatform.Abstractions.Agent.Context;

using DevOpsMigrationPlatform.Abstractions.Agent.Import;

using DevOpsMigrationPlatform.Abstractions.Agent.Tools;

using DevOpsMigrationPlatform.Abstractions.Jobs;

using DevOpsMigrationPlatform.Abstractions.Options;

using DevOpsMigrationPlatform.Abstractions.Storage;

using DevOpsMigrationPlatform.Abstractions.Streaming;

using DevOpsMigrationPlatform.Infrastructure.Agent.Modules;

using DevOpsMigrationPlatform.Infrastructure.Agent.Tests.TestUtilities;

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



public class ReplicateSourceTreeContext

{

    private const string SourceOrganisation = "https://dev.azure.com/fabrikam";

    private const string SourceProject = "SourceProject";



    private static readonly JsonSerializerOptions s_jsonOptions = new()

    {

        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,

        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,

        PropertyNameCaseInsensitive = true

    };



    public bool SourceTreeArtifactAbsent { get; set; }



    private readonly List<string> _areaNodes = new();

    private readonly List<IterationNodeEntry> _iterationNodes = new();

    private readonly HashSet<string> _checkpointedPaths = new(StringComparer.OrdinalIgnoreCase);



    public bool SetIterationDatesThrows { get; set; }

    public Exception? CaughtException { get; private set; }



    public Mock<INodeCreator> NodeCreatorMock { get; } = new(MockBehavior.Loose);

    public Mock<IPackageAccess> PackageMock { get; } = PackageTestFactory.CreateLooseMock();



    public ReplicateSourceTreeContext()

    {

        NodeCreatorMock.Setup(c => c.EnsureExistsAsync(

            It.IsAny<ClassificationNodeType>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))

            .Returns(Task.CompletedTask);

        NodeCreatorMock.Setup(c => c.SetIterationDatesAsync(

            It.IsAny<string>(), It.IsAny<DateTimeOffset?>(), It.IsAny<DateTimeOffset?>(), It.IsAny<CancellationToken>()))

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

            SetupPackageContent("source-tree.json", null);

        }

        else

        {

            var snapshot = new ClassificationTreeSnapshot(_areaNodes, _iterationNodes);

            SetupPackageContent("source-tree.json", JsonSerializer.Serialize(snapshot, s_jsonOptions));

        }



        if (_checkpointedPaths.Count > 0)

        {

            var progress = new NodeReplicationProgress();

            foreach (var path in _checkpointedPaths)

                progress.ReplicatedPaths.Add(path);



            SetupPackageContent("replication-progress.json", JsonSerializer.Serialize(progress, s_jsonOptions));

        }

        else

        {

            SetupPackageContent("replication-progress.json", null);

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



        var opts = new NodeTranslationOptions

        {

            Enabled = true,

            AreaPathMappings = [],

            IterationPathMappings = []

        };



        var tool = new NodeTranslationTool(Options.Create(opts), NullLogger<NodeTranslationTool>.Instance);

        var optionsMonitor = new Mock<IOptionsMonitor<NodeTranslationOptions>>();

        optionsMonitor.SetupGet(o => o.CurrentValue).Returns(opts);



        var orchestrator = new NodesOrchestrator(

            NullLogger<NodesOrchestrator>.Instance,

            tool,

            NodeCreatorMock.Object,

            optionsMonitor.Object,

            package: PackageMock.Object);



        var sourceEndpoint = Mock.Of<ISourceEndpointInfo>(e => e.Project == SourceProject && e.Url == SourceOrganisation);

        var targetEndpoint = Mock.Of<ITargetEndpointInfo>(e => e.Project == "TargetProject");

        var importContext = new ImportContext

        {

            Job = new Job { Kind = JobKind.Import },

            Package = PackageMock.Object,

            ProgressSink = Mock.Of<IProgressSink>()

        };



        try

        {

            await orchestrator.ImportAsync(importContext, sourceEndpoint, targetEndpoint, null, true, CancellationToken.None);

        }

        catch (Exception ex)

        {

            CaughtException = ex;

        }

    }



    private void SetupPackageContent(string relativePath, string? content)

    {

        PackageMock.Setup(p => p.RequestContentAsync(

                It.Is<PackageContentContext>(c => IsNodesPath(c, relativePath)),

                It.IsAny<CancellationToken>()))

            .Returns(() => ToPayload(content));

    }



    private static bool IsNodesPath(PackageContentContext context, string relativePath)

        => string.Equals(context.Address?.RelativePath, relativePath, StringComparison.OrdinalIgnoreCase)

            || string.Equals(context.Address?.RelativePath, $"Nodes/{relativePath}", StringComparison.OrdinalIgnoreCase);



    private static ValueTask<PackagePayload?> ToPayload(string? json)

    {

        if (json is null)

            return ValueTask.FromResult<PackagePayload?>(null);



        var bytes = System.Text.Encoding.UTF8.GetBytes(json);

        return ValueTask.FromResult<PackagePayload?>(new PackagePayload(new System.IO.MemoryStream(bytes), "application/json"));

    }

}





