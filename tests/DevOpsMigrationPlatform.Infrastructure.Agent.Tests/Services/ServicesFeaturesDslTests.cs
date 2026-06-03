// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Agent.Discovery;
using DevOpsMigrationPlatform.Abstractions.Agent.Tools;
using DevOpsMigrationPlatform.Abstractions.Jobs;
using DevOpsMigrationPlatform.Abstractions.Options;
using DevOpsMigrationPlatform.Abstractions.Organisations;
using DevOpsMigrationPlatform.Abstractions.Storage;
using DevOpsMigrationPlatform.Infrastructure.Agent.Context;
using DevOpsMigrationPlatform.Infrastructure.Agent.Discovery;
using DevOpsMigrationPlatform.Infrastructure.Agent.WorkItems;
using DevOpsMigrationPlatform.Infrastructure.Agent.Modules;
using DevOpsMigrationPlatform.Infrastructure.Agent.Tests.TestUtilities;
using Microsoft.Extensions.Configuration;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Tests.Services;

[TestClass]
public sealed class ServicesFeaturesDslTests
{
    [TestCategory("UnitTest")]
    [TestMethod]
    public void IdentityResolution_MappedIdentity_ResolvesMappedTargetIdentity()
    {
        var package = PackageTestFactory.CreateLooseMock().Object;
        var sut = new FileSystemIdentityMappingService(
            new Dictionary<string, string> { ["jsmith@source.example.com"] = "john.smith@target.example.com" },
            "migration-bot@target.example.com",
            package,
            "test-org",
            "test-project");

        var resolved = sut.Resolve("jsmith@source.example.com");

        Assert.AreEqual("john.smith@target.example.com", resolved);
    }

    [TestCategory("UnitTest")]
    [TestMethod]
    public async Task IdentityResolution_UnmappedIdentity_FallsBackAndWritesWarning()
    {
        var persistedPaths = new List<string>();
        var package = PackageTestFactory.CreateLooseMock();
        package
            .Setup(p => p.PersistContentAsync(It.IsAny<PackageContentContext>(), It.IsAny<PackagePayload>(), It.IsAny<CancellationToken>()))
            .Returns<PackageContentContext, PackagePayload, CancellationToken>((ctx, _, _) =>
            {
                persistedPaths.Add(ctx.Address!.RelativePath);
                return ValueTask.CompletedTask;
            });

        var sut = new FileSystemIdentityMappingService(
            new Dictionary<string, string>(),
            "migration-bot@target.example.com",
            package.Object,
            "test-org",
            "test-project");

        var resolved = sut.Resolve("legacy@old.example.com");
        await sut.FlushWarningsAsync(CancellationToken.None);

        Assert.AreEqual("migration-bot@target.example.com", resolved);
        Assert.IsTrue(
            persistedPaths.Any(p => p.Contains("identity-warnings/", StringComparison.Ordinal)),
            "Expected unresolved identity warning artefact.");
    }

    [TestCategory("UnitTest")]
    [TestMethod]
    public async Task IdentityResolution_ImportStageAppliesMappedIdentity()
    {
        var (processor, target, mapping) = CreateRevisionProcessor(
            "System.AssignedTo",
            "user@source.com",
            toolSetup: tool => tool.Setup(t => t.Resolve("user@source.com")).Returns("user@target.com"));

        await processor.ProcessAsync(
            "WorkItems/2024-01-01/00000638000000000001-1-0",
            new WorkItemsModuleExtensions(),
            resumeAtStage: null,
            Mock.Of<IWorkItemResolutionStrategy>(),
            CancellationToken.None);

        target.Verify(
            t => t.UpdateFieldsAsync(
                It.IsAny<int>(),
                It.Is<IReadOnlyList<WorkItemField>>(f =>
                    f.Any(x => x.ReferenceName == "System.AssignedTo" && (string?)x.Value == "user@target.com")),
                It.IsAny<CancellationToken>()),
            Times.Once);
        mapping.Verify(t => t.Resolve("user@source.com"), Times.Once);
    }

    [TestCategory("UnitTest")]
    [TestMethod]
    public async Task IdentityResolution_ImportStageWithoutMapping_PassesValueThrough()
    {
        var (processor, target, mapping) = CreateRevisionProcessor(
            "System.CreatedBy",
            "someuser@domain.com",
            toolSetup: tool => tool.Setup(t => t.Resolve(It.IsAny<string>())).Returns<string>(s => s));

        await processor.ProcessAsync(
            "WorkItems/2024-01-01/00000638000000000001-1-0",
            new WorkItemsModuleExtensions(),
            resumeAtStage: null,
            Mock.Of<IWorkItemResolutionStrategy>(),
            CancellationToken.None);

        target.Verify(
            t => t.UpdateFieldsAsync(
                It.IsAny<int>(),
                It.Is<IReadOnlyList<WorkItemField>>(f =>
                    f.Any(x => x.ReferenceName == "System.CreatedBy" && (string?)x.Value == "someuser@domain.com")),
                It.IsAny<CancellationToken>()),
            Times.Once);
        mapping.Verify(t => t.Resolve("someuser@domain.com"), Times.Once);
    }

    [TestCategory("UnitTest")]
    [TestMethod]
    public void IdentityResolution_WorkItemsImport_DeclaresIdentitiesPrerequisite()
    {
        var sourceEndpoint = new Mock<DevOpsMigrationPlatform.Abstractions.Agent.Context.ISourceEndpointInfo>(MockBehavior.Loose);
        sourceEndpoint.SetupGet(e => e.OrganisationSlug).Returns("source-org");
        sourceEndpoint.SetupGet(e => e.Project).Returns("ProjectA");

        var module = new WorkItemsModule(
            sourceFactory: Mock.Of<DevOpsMigrationPlatform.Abstractions.Agent.Export.IWorkItemRevisionSourceFactory>(),
            logger: Microsoft.Extensions.Logging.Abstractions.NullLogger<WorkItemsModule>.Instance,
            options: Microsoft.Extensions.Options.Options.Create(new WorkItemsModuleOptions()),
            sourceEndpointInfo: sourceEndpoint.Object,
            orchestratorLogger: Microsoft.Extensions.Logging.Abstractions.NullLogger<WorkItemsImportRuntime>.Instance,
            importTargetFactory: Mock.Of<DevOpsMigrationPlatform.Abstractions.Agent.WorkItems.IWorkItemTargetFactory>(),
            resolutionStrategyFactory: Mock.Of<DevOpsMigrationPlatform.Abstractions.Agent.WorkItems.IWorkItemResolutionStrategyFactory>(),
            checkpointingFactory: Mock.Of<DevOpsMigrationPlatform.Abstractions.Agent.Checkpointing.ICheckpointingServiceFactory>(),
            idMapStoreFactory: Mock.Of<DevOpsMigrationPlatform.Abstractions.Storage.IIdMapStoreFactory>(),
            processorFactory: Mock.Of<DevOpsMigrationPlatform.Abstractions.Agent.Export.IWorkItemResolutionProcessorFactory>(),
            targetEndpointInfo: Mock.Of<DevOpsMigrationPlatform.Abstractions.Agent.Context.ITargetEndpointInfo>(),
            identityMappingService: Mock.Of<DevOpsMigrationPlatform.Abstractions.Agent.Identity.IIdentityMappingService>(),
            nodeTranslationTool: Mock.Of<DevOpsMigrationPlatform.Abstractions.Agent.Tools.INodeTranslationTool>(),
            fieldTransformTool: Mock.Of<DevOpsMigrationPlatform.Abstractions.Agent.Tools.IFieldTransformTool>());

        Assert.IsTrue(module.DependsOn.Any(d => d.ModuleType == typeof(IdentitiesModule) && d.AppliesToImport));
    }

    [TestCategory("UnitTest")]
    [TestMethod]
    public void OrganisationEntryConversion_ToEndpoint_PreservesResolvedValues()
    {
        const string envVarName = "NKD_TEST_SERVICES_PAT";
        Environment.SetEnvironmentVariable(envVarName, "env-token-123");
        try
        {
            var entry = new AzureDevOpsOrganisationEntry
            {
                Type = "AzureDevOpsServices",
                Url = "https://dev.azure.com/contoso",
                ApiVersion = "7.1",
                Authentication = new EndpointAuthenticationOptions
                {
                    Type = AuthenticationType.AccessToken,
                    AccessToken = $"$ENV:{envVarName}"
                }
            };

            var endpoint = entry.ToEndpointOptions().ToOrganisationEndpoint();

            Assert.AreEqual("https://dev.azure.com/contoso", endpoint.ResolvedUrl);
            Assert.AreEqual(AuthenticationType.AccessToken, endpoint.Authentication.Type);
            Assert.AreEqual("env-token-123", endpoint.Authentication.ResolvedAccessToken);
            Assert.AreEqual("7.1", endpoint.ApiVersion);
        }
        finally
        {
            Environment.SetEnvironmentVariable(envVarName, null);
        }
    }

    [TestCategory("UnitTest")]
    [TestMethod]
    public void OrganisationEntryConversion_WindowsAuthentication_HasNoAccessToken()
    {
        var entry = new AzureDevOpsOrganisationEntry
        {
            Type = "AzureDevOpsServices",
            Url = "https://dev.azure.com/contoso",
            Authentication = new EndpointAuthenticationOptions
            {
                Type = AuthenticationType.Windows,
                AccessToken = null
            }
        };

        var endpoint = entry.ToEndpointOptions().ToOrganisationEndpoint();

        Assert.AreEqual(AuthenticationType.Windows, endpoint.Authentication.Type);
        Assert.IsNull(endpoint.Authentication.ResolvedAccessToken);
    }

    [TestCategory("UnitTest")]
    [TestMethod]
    public void OrganisationEndpointServiceInterfaces_UseEndpointConnectionContextOnly()
    {
        var serviceInterfaces = typeof(IWorkItemDiscoveryService).Assembly
            .GetTypes()
            .Where(t => t.IsInterface && t.Name.EndsWith("Service", StringComparison.Ordinal))
            .ToList();

        foreach (var iface in serviceInterfaces)
        {
            foreach (var method in iface.GetMethods())
            {
                var hasEndpoint = method.GetParameters().Any(p => p.ParameterType == typeof(OrganisationEndpoint));
                if (!hasEndpoint)
                    continue;

                Assert.IsFalse(
                    method.GetParameters().Any(p =>
                        p.ParameterType == typeof(string) &&
                        (p.Name?.Contains("token", StringComparison.OrdinalIgnoreCase) == true ||
                         p.Name?.Contains("access", StringComparison.OrdinalIgnoreCase) == true)),
                    $"{iface.Name}.{method.Name} should not require separate access token parameters.");
            }
        }
    }

    [TestCategory("UnitTest")]
    [TestMethod]
    public void DiscoveryOrganisationScope_BuildsScopedEndpointsWithProjectsAndAuthentication()
    {
        const string tokenEnvVar = "NKD_TEST_DISCOVERY_PAT";
        Environment.SetEnvironmentVariable(tokenEnvVar, "discovery-token");
        try
        {
            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(
                    new Dictionary<string, string?>
                    {
                        ["MigrationPlatform:Organisations:0:Type"] = "AzureDevOpsServices",
                        ["MigrationPlatform:Organisations:0:Url"] = "https://dev.azure.com/fabrikam",
                        ["MigrationPlatform:Organisations:0:Authentication:Type"] = "AccessToken",
                        ["MigrationPlatform:Organisations:0:Authentication:AccessToken"] = $"$ENV:{tokenEnvVar}",
                        ["MigrationPlatform:Organisations:0:Projects:0"] = "ProjectA",
                        ["MigrationPlatform:Organisations:0:Projects:1"] = "ProjectB"
                    })
                .Build();

            var method = typeof(JobExecutionPlanBuilder).GetMethod(
                "BuildOrganisationEndpoints",
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert.IsNotNull(method, "BuildOrganisationEndpoints should be available for discovery scope construction.");

            var organisations = (IReadOnlyList<ScopedOrganisationEndpoint>)method!.Invoke(null, [config])!;

            Assert.AreEqual(1, organisations.Count);
            CollectionAssert.AreEqual(new[] { "ProjectA", "ProjectB" }, organisations[0].Projects);
            var endpoint = organisations[0].Endpoint.ToOrganisationEndpoint();
            Assert.AreEqual("https://dev.azure.com/fabrikam", endpoint.ResolvedUrl);
            Assert.AreEqual(AuthenticationType.AccessToken, endpoint.Authentication.Type);
            Assert.AreEqual("discovery-token", endpoint.Authentication.ResolvedAccessToken);
        }
        finally
        {
            Environment.SetEnvironmentVariable(tokenEnvVar, null);
        }
    }

    private static (
        WorkItemResolutionProcessor Processor,
        Mock<DevOpsMigrationPlatform.Abstractions.Agent.WorkItems.IWorkItemTarget> Target,
        Mock<IIdentityLookupTool> IdentityTool)
        CreateRevisionProcessor(string fieldName, string fieldValue, Action<Mock<IIdentityLookupTool>> toolSetup)
    {
        var target = new Mock<DevOpsMigrationPlatform.Abstractions.Agent.WorkItems.IWorkItemTarget>(MockBehavior.Strict);
        var idMap = new Mock<IIdMapStore>(MockBehavior.Strict);
        var checkpointing = new Mock<DevOpsMigrationPlatform.Abstractions.Agent.Checkpointing.ICheckpointingService>(MockBehavior.Strict);
        var identityTool = new Mock<IIdentityLookupTool>(MockBehavior.Strict);
        var package = PackageTestFactory.CreateLooseMock();

        toolSetup(identityTool);
        identityTool.Setup(t => t.IsEnabled).Returns(true);

        var revisionJson = $$"""
        {
          "WorkItemId": 1,
          "RevisionIndex": 0,
          "Fields": [
            {"ReferenceName": "System.WorkItemType", "Value": "Task"},
            {"ReferenceName": "{{fieldName}}", "Value": "{{fieldValue}}"}
          ],
          "Attachments": [],
          "RelatedLinks": [],
          "ExternalLinks": [],
          "Hyperlinks": [],
          "EmbeddedImages": []
        }
        """;

        var folder = "WorkItems/2024-01-01/00000638000000000001-1-0";
        package
            .Setup(p => p.RequestContentAsync(
                It.Is<PackageContentContext>(c => c.Address != null && c.Address.RelativePath.EndsWith($"{folder.Replace("WorkItems/", string.Empty)}/revision.json", StringComparison.Ordinal)),
                It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                var bytes = System.Text.Encoding.UTF8.GetBytes(revisionJson);
                return ValueTask.FromResult<PackagePayload?>(new PackagePayload(new MemoryStream(bytes, writable: false), "application/json"));
            });
        package
            .Setup(p => p.RequestContentAsync(
                It.Is<PackageContentContext>(c => c.Address != null && c.Address.RelativePath.EndsWith($"{folder.Replace("WorkItems/", string.Empty)}/comment.json", StringComparison.Ordinal)),
                It.IsAny<CancellationToken>()))
            .Returns(ValueTask.FromResult<PackagePayload?>(null));

        checkpointing
            .Setup(s => s.WriteCursorAsync("import.workitems", It.IsAny<CursorEntry>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        idMap
            .Setup(s => s.GetTargetWorkItemIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync((int?)null);
        idMap
            .Setup(s => s.SetWorkItemMappingAsync(1, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        idMap
            .Setup(s => s.GetAttachmentIdAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);
        idMap
            .Setup(s => s.DisposeAsync())
            .Returns(new ValueTask());

        target
            .Setup(t => t.CreateWorkItemAsync(It.IsAny<string>(), It.IsAny<IReadOnlyList<WorkItemField>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ImportedWorkItemResult { TargetWorkItemId = 10, IsNewlyCreated = true });
        idMap
            .Setup(s => s.GetTargetWorkItemIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(10);
        target
            .Setup(t => t.UpdateFieldsAsync(It.IsAny<int>(), It.IsAny<IReadOnlyList<WorkItemField>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        target
            .Setup(t => t.AddLinksAsync(It.IsAny<int>(), It.IsAny<IReadOnlyList<RelatedWorkItemLink>>(), It.IsAny<IReadOnlyList<ExternalWorkItemLink>>(), It.IsAny<IReadOnlyList<HyperlinkWorkItemLink>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        target
            .Setup(t => t.WorkItemExistsAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var strategy = new Mock<IWorkItemResolutionStrategy>(MockBehavior.Strict);
        strategy
            .Setup(s => s.ResolveSingleAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((int?)null);
        strategy
            .Setup(s => s.WriteProvenanceAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var processor = new WorkItemResolutionProcessor(
            target.Object,
            idMap.Object,
            checkpointing.Object,
            identityTool.Object,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<WorkItemResolutionProcessor>.Instance,
            "https://dev.azure.com/contoso",
            "Shop",
            package: package.Object);

        return (processor, target, identityTool);
    }
}
