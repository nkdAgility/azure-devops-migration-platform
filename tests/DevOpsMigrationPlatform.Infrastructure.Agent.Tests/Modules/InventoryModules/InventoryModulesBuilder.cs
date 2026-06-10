// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System.Threading;
using System.Threading.Tasks;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Tests.Modules.InventoryModules;

/// <summary>
/// Arranges an inventory job with configurable module and analyser inclusion.
/// By default all four inventory-capable data modules are enabled.
/// </summary>
public sealed class InventoryModulesBuilder
{
    private bool _includeWorkItems   = true;
    private bool _includeIdentities  = true;
    private bool _includeNodes       = true;
    private bool _includeTeams       = true;
    private bool _includeInventoryAnalyser = true;

    internal InventoryModulesBuilder() { }

    // --- module inclusion toggles ---

    /// <summary>Removes the InventoryAnalyser post-processor from the job.</summary>
    public InventoryModulesBuilder WithoutInventoryAnalyser()
    {
        _includeInventoryAnalyser = false;
        return this;
    }

    /// <summary>
    /// Removes the InventoryDiscoveryModule from the job.
    /// In the production pipeline InventoryDiscoveryModule is the InventoryAnalyser;
    /// this method is the feature-vocabulary alias for <see cref="WithoutInventoryAnalyser"/>.
    /// </summary>
    public InventoryModulesBuilder WithoutInventoryDiscoveryModule()
        => WithoutInventoryAnalyser();

    /// <summary>Removes the named data module from the job.</summary>
    public InventoryModulesBuilder WithoutModule(string moduleName)
    {
        switch (moduleName)
        {
            case "WorkItems":   _includeWorkItems  = false; break;
            case "Identities":  _includeIdentities = false; break;
            case "Nodes":       _includeNodes      = false; break;
            case "Teams":       _includeTeams      = false; break;
            default:
                throw new System.ArgumentException($"Unknown module name: '{moduleName}'.", nameof(moduleName));
        }
        return this;
    }

    // --- execution ---

    /// <summary>
    /// Executes the inventory job and returns a result wrapper for assertions.
    /// </summary>
    public Task<InventoryModulesResult> RunAsync(CancellationToken cancellationToken = default)
        => InventoryModulesDriver.RunAsync(
            includeWorkItems:          _includeWorkItems,
            includeIdentities:         _includeIdentities,
            includeNodes:              _includeNodes,
            includeTeams:              _includeTeams,
            includeInventoryAnalyser:  _includeInventoryAnalyser,
            cancellationToken:         cancellationToken);
}
