// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited.

using DevOpsMigrationPlatform.Abstractions.Agent.Modules;

using DevOpsMigrationPlatform.Abstractions.Agent.Tools;

using DevOpsMigrationPlatform.Abstractions.Options;

using DevOpsMigrationPlatform.Abstractions.Storage;

using DevOpsMigrationPlatform.Infrastructure.Agent.Modules;

using DevOpsMigrationPlatform.Infrastructure.Agent.Tests.TestUtilities;

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

    public Mock<IPackageAccess> PackageMock { get; } = PackageTestFactory.CreateLooseMock();

    private INodesOrchestrator? _orchestrator;



    public AutoCreateNodesContext()

    {

        NodeCreatorMock.Setup(c => c.EnsureExistsAsync(

            It.IsAny<ClassificationNodeType>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))

            .Returns(Task.CompletedTask);

    }



    public void AddAreaPath(string path) => _areaPaths.Add(path);

    public void AddIterationPath(string path) => _iterationPaths.Add(path);



    public void SetupArtifact()

    {

        var artifact = new ReferencedPathsArtifact(_areaPaths, _iterationPaths);

        SetupPackageContent("referenced-paths.json", JsonSerializer.Serialize(artifact, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));

        SetupPackageContent("source-tree.json", null);

    }



    public void SetupEmptyArtifact()

    {

        var artifact = new ReferencedPathsArtifact(new List<string>(), new List<string>());

        SetupPackageContent("referenced-paths.json", JsonSerializer.Serialize(artifact, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));

        SetupPackageContent("source-tree.json", null);

    }



    public INodesOrchestrator BuildOrchestrator()

    {

        var opts = new NodeTranslationOptions

        {

            Enabled = true,

            AutoCreateNodes = AutoCreateNodesEnabled,

            AreaPathMappings = [],

            IterationPathMappings = []

        };

        var tool = new NodeTranslationTool(Options.Create(opts), NullLogger<NodeTranslationTool>.Instance);

        var optionsMonitor = new Mock<IOptionsMonitor<NodeTranslationOptions>>();

        optionsMonitor.SetupGet(o => o.CurrentValue).Returns(opts);

        _orchestrator = new NodesOrchestrator(

            NullLogger<NodesOrchestrator>.Instance,

            tool,

            NodeCreatorMock.Object,

            optionsMonitor.Object,

            package: PackageMock.Object);

        return _orchestrator;

    }



    public INodesOrchestrator GetOrchestrator() => _orchestrator ?? BuildOrchestrator();



    private void SetupPackageContent(string relativePath, string? content)

    {

        PackageMock.Setup(p => p.RequestContentAsync(

                It.Is<PackageContentContext>(c => IsNodesPath(c, relativePath)),

                It.IsAny<CancellationToken>()))

            .Returns(() => ToPayload(content));

    }



    private static bool IsNodesPath(PackageContentContext context, string relativePath)

        => string.Equals(context.Address?.RelativePath, relativePath, System.StringComparison.OrdinalIgnoreCase)

            || string.Equals(context.Address?.RelativePath, $"Nodes/{relativePath}", System.StringComparison.OrdinalIgnoreCase);



    private static ValueTask<PackagePayload?> ToPayload(string? json)

    {

        if (json is null)

            return ValueTask.FromResult<PackagePayload?>(null);



        var bytes = System.Text.Encoding.UTF8.GetBytes(json);

        return ValueTask.FromResult<PackagePayload?>(new PackagePayload(new System.IO.MemoryStream(bytes), "application/json"));

    }

}





