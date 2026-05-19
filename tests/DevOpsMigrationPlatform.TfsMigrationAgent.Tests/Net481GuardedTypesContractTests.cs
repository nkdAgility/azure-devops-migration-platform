// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DevOpsMigrationPlatform.TfsMigrationAgent.Tests;

[TestClass]
[TestCategory("NET481")]
public class Net481GuardedTypesContractTests
{
    private static readonly (string AssemblyName, string FullTypeName)[] ExpectedTypes =
    [
        ("DevOpsMigrationPlatform.Abstractions.Agent", "DevOpsMigrationPlatform.Abstractions.Agent.Tools.ITeamTarget"),
        ("DevOpsMigrationPlatform.Abstractions.Agent", "DevOpsMigrationPlatform.Abstractions.Agent.Tools.IReferencedPathTracker"),
        ("DevOpsMigrationPlatform.Infrastructure", "DevOpsMigrationPlatform.Infrastructure.Serialization.PolymorphicOrganisationEntryConverter"),
        ("DevOpsMigrationPlatform.Infrastructure", "DevOpsMigrationPlatform.Infrastructure.Serialization.PolymorphicEndpointOptionsConverter"),
        ("DevOpsMigrationPlatform.Infrastructure", "DevOpsMigrationPlatform.Infrastructure.Serialization.EndpointOptionsTypeRegistry"),
        ("DevOpsMigrationPlatform.Infrastructure", "DevOpsMigrationPlatform.Infrastructure.Serialization.EndpointOptionsRegistrationExtensions"),
        ("DevOpsMigrationPlatform.Infrastructure", "DevOpsMigrationPlatform.Infrastructure.Serialization.EndpointOptionsRegistration"),
        ("DevOpsMigrationPlatform.Infrastructure", "DevOpsMigrationPlatform.Infrastructure.Config.ConfigurationService"),
        ("DevOpsMigrationPlatform.Infrastructure.Agent", "DevOpsMigrationPlatform.Infrastructure.Agent.Connectors.FactoryRegistrationExtensions"),
        ("DevOpsMigrationPlatform.Infrastructure.Agent", "DevOpsMigrationPlatform.Infrastructure.Agent.Connectors.CompositeWorkItemDiscoveryService"),
        ("DevOpsMigrationPlatform.Infrastructure.Agent", "DevOpsMigrationPlatform.Infrastructure.Agent.Connectors.KeyedWorkItemDiscoveryService"),
        ("DevOpsMigrationPlatform.Infrastructure.Agent", "DevOpsMigrationPlatform.Infrastructure.Agent.Connectors.CompositeTeamTarget"),
        ("DevOpsMigrationPlatform.Infrastructure.Agent", "DevOpsMigrationPlatform.Infrastructure.Agent.Connectors.KeyedTeamTarget"),
        ("DevOpsMigrationPlatform.Infrastructure.Agent", "DevOpsMigrationPlatform.Infrastructure.Agent.Connectors.ActiveJobSourceEndpointInfo"),
        ("DevOpsMigrationPlatform.Infrastructure.Agent", "DevOpsMigrationPlatform.Infrastructure.Agent.Connectors.ActiveJobTargetEndpointInfo"),
        ("DevOpsMigrationPlatform.Infrastructure.Agent", "DevOpsMigrationPlatform.Infrastructure.Agent.Connectors.ActiveJobAgentJobContext"),
        ("DevOpsMigrationPlatform.Infrastructure.Agent", "DevOpsMigrationPlatform.Infrastructure.Agent.Export.CompositeWorkItemRevisionSourceFactory"),
        ("DevOpsMigrationPlatform.Infrastructure.Agent", "DevOpsMigrationPlatform.Infrastructure.Agent.Export.KeyedWorkItemRevisionSourceFactory"),
        ("DevOpsMigrationPlatform.Infrastructure.Agent", "DevOpsMigrationPlatform.Infrastructure.Agent.Teams.TeamImportOrchestrator"),
        ("DevOpsMigrationPlatform.Infrastructure.Agent", "DevOpsMigrationPlatform.Infrastructure.Agent.Import.CompositeWorkItemTypeReadinessTargetFactory"),
        ("DevOpsMigrationPlatform.Infrastructure.Agent", "DevOpsMigrationPlatform.Infrastructure.Agent.Import.KeyedWorkItemTypeReadinessTargetFactory"),
        ("DevOpsMigrationPlatform.Infrastructure.Agent", "DevOpsMigrationPlatform.Infrastructure.Agent.Import.CompositeWorkItemResolutionStrategyFactory"),
        ("DevOpsMigrationPlatform.Infrastructure.Agent", "DevOpsMigrationPlatform.Infrastructure.Agent.Import.KeyedWorkItemResolutionStrategyFactory"),
        ("DevOpsMigrationPlatform.Infrastructure.Agent", "DevOpsMigrationPlatform.Infrastructure.Agent.Import.CompositeWorkItemImportTargetFactory"),
        ("DevOpsMigrationPlatform.Infrastructure.Agent", "DevOpsMigrationPlatform.Infrastructure.Agent.Import.KeyedWorkItemImportTargetFactory"),
        ("DevOpsMigrationPlatform.Infrastructure.Agent", "DevOpsMigrationPlatform.Infrastructure.Agent.Import.NullResolutionStrategy"),
        ("DevOpsMigrationPlatform.Infrastructure.Agent", "DevOpsMigrationPlatform.Infrastructure.Agent.Import.PassThroughIdentityMappingService"),
        ("DevOpsMigrationPlatform.Infrastructure.Agent", "DevOpsMigrationPlatform.Infrastructure.Agent.Tools.IdentityLookup.IdentityLookupToolServiceCollectionExtensions"),
        ("DevOpsMigrationPlatform.Infrastructure.Agent", "DevOpsMigrationPlatform.Infrastructure.Agent.Tools.IdentityLookup.IdentityLookupTool"),
        ("DevOpsMigrationPlatform.Infrastructure.Agent", "DevOpsMigrationPlatform.Infrastructure.Agent.Tools.NodeTranslation.ReferencedPathTracker"),
        ("DevOpsMigrationPlatform.Infrastructure.Agent", "DevOpsMigrationPlatform.Infrastructure.Agent.Tools.NodeTranslation.NodeTranslationValidator"),
        ("DevOpsMigrationPlatform.Infrastructure.Agent", "DevOpsMigrationPlatform.Infrastructure.Agent.Tools.NodeTranslation.NodeTranslationTool"),
        ("DevOpsMigrationPlatform.Infrastructure.Agent", "DevOpsMigrationPlatform.Infrastructure.Agent.Tools.NodeTranslation.NodeTranslationOptionsValidator"),
        ("DevOpsMigrationPlatform.Infrastructure.Agent", "DevOpsMigrationPlatform.Infrastructure.Agent.Tools.NodeTranslation.CompositeNodeCreator"),
        ("DevOpsMigrationPlatform.Infrastructure.Agent", "DevOpsMigrationPlatform.Infrastructure.Agent.Tools.NodeTranslation.KeyedNodeCreator"),
        ("DevOpsMigrationPlatform.Infrastructure.Agent", "DevOpsMigrationPlatform.Infrastructure.Agent.Tools.NodeTranslation.CompositeClassificationTreeReader"),
        ("DevOpsMigrationPlatform.Infrastructure.Agent", "DevOpsMigrationPlatform.Infrastructure.Agent.Tools.NodeTranslation.KeyedClassificationTreeReader")
    ];

    [TestMethod]
    public void Type_IsAvailable_ForNet481Build()
    {
        foreach (var expected in ExpectedTypes)
        {
            var assembly = Assembly.Load(expected.AssemblyName);
            var type = assembly.GetType(expected.FullTypeName, throwOnError: false);
            Assert.IsNotNull(type, $"{expected.FullTypeName} should exist in {expected.AssemblyName} for net481.");
        }
    }
}
