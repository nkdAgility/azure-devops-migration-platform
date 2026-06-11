// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions.Agent.Analysis;
using DevOpsMigrationPlatform.Abstractions.Agent.Modules;
using DevOpsMigrationPlatform.Abstractions.Jobs;
using DevOpsMigrationPlatform.Abstractions.Organisations;
using DevOpsMigrationPlatform.Infrastructure.Agent.Tests.TestUtilities;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Tests.Modules.InventoryModules;

/// <summary>
/// Internal execution harness. Constructs the module instances, wires a shared
/// <see cref="DevOpsMigrationPlatform.Abstractions.Storage.IPackageAccess"/> mock, calls
/// <c>CaptureAsync</c> on each active module, and returns the package state wrapped in
/// <see cref="InventoryModulesResult"/>.
/// </summary>
internal static class InventoryModulesDriver
{
    internal static async Task<InventoryModulesResult> RunAsync(
        bool includeWorkItems,
        bool includeIdentities,
        bool includeNodes,
        bool includeTeams,
        bool includeInventoryAnalyser,
        CancellationToken cancellationToken)
    {
        var packageMock = PackageTestFactory.CreateLooseMock();

        // Build the active module list. Order mirrors production dispatch order.
        var modules = new List<ICapture>();
        if (includeWorkItems)   modules.Add(InventoryModuleFactory.CreateWorkItemsModule(packageMock));
        if (includeNodes)       modules.Add(InventoryModuleFactory.CreateNodesModule());
        if (includeIdentities)  modules.Add(InventoryModuleFactory.CreateIdentitiesModule());
        if (includeTeams)       modules.Add(InventoryModuleFactory.CreateTeamsModule());

        var context = CreateContext(packageMock.Object);

        foreach (var module in modules)
            await module.CaptureAsync(context, cancellationToken).ConfigureAwait(false);

        // InventoryAnalyser is an IAnalyser, not an IModule. When included it
        // runs after the data modules. Its absence must not prevent artefact production.
        // For the "without InventoryAnalyser" variant we intentionally skip this step
        // to prove that data-module artefacts are produced independently.
        if (includeInventoryAnalyser)
        {
            var analyser = InventoryModuleFactory.CreateInventoryAnalyser();
            var analyseContext = new AnalyseContext
            {
                Job     = CreateJob(),
                Package = packageMock.Object
            };
            await analyser.AnalyseAsync(analyseContext, cancellationToken).ConfigureAwait(false);
        }

        return new InventoryModulesResult(packageMock, includeInventoryAnalyser);
    }

    private static InventoryContext CreateContext(DevOpsMigrationPlatform.Abstractions.Storage.IPackageAccess package)
        => new()
        {
            Job             = CreateJob(),
            Package         = package,
            ProgressSink    = null,
            SourceEndpoint  = new OrganisationEndpoint { Type = "Simulated", ResolvedUrl = "https://source.example" },
            Project         = "ProjectA"
        };

    private static Job CreateJob()
        => new() { JobId = "inventory-modules-test", Kind = JobKind.Inventory };
}
