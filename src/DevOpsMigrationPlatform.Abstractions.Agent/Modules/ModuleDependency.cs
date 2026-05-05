// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;

namespace DevOpsMigrationPlatform.Abstractions.Agent.Modules;

/// <summary>
/// Represents a typed dependency between modules, specifying the target module
/// and the phase(s) during which the dependency applies.
/// </summary>
/// <param name="ModuleType">The module type that this module depends on.</param>
/// <param name="Phase">The phase(s) during which this dependency applies.</param>
public sealed record ModuleDependency(Type ModuleType, DependencyPhase Phase)
{
    /// <summary>
    /// Optional explicit module name override (primarily for testing).
    /// When null, the name is computed from ModuleType.
    /// </summary>
    public string? ModuleNameOverride { get; init; }

    /// <summary>
    /// Gets the module name from the module type or the override.
    /// Extracts the name by removing "Module" or "Analyser" suffix if present.
    /// </summary>
    public string ModuleName
    {
        get
        {
            if (ModuleNameOverride is not null)
                return ModuleNameOverride;

            var typeName = ModuleType.Name;
            if (typeName.EndsWith("Module", StringComparison.Ordinal))
                return typeName.Substring(0, typeName.Length - 6);

            if (typeName.EndsWith("Analyser", StringComparison.Ordinal))
                return typeName.Substring(0, typeName.Length - 8);

            return typeName;
        }
    }

    /// <summary>
    /// Returns true if this dependency applies during the Inventory phase.
    /// </summary>
    public bool AppliesToInventory => Phase is DependencyPhase.Inventory;

    /// <summary>
    /// Returns true if this dependency applies during the Export phase.
    /// </summary>
    public bool AppliesToExport => Phase is DependencyPhase.Export or DependencyPhase.Both;

    /// <summary>
    /// Returns true if this dependency applies during the Import phase.
    /// </summary>
    public bool AppliesToImport => Phase is DependencyPhase.Import or DependencyPhase.Both;

    /// <summary>
    /// Returns true if this dependency applies during the Prepare phase.
    /// </summary>
    public bool AppliesToPrepare => Phase is DependencyPhase.Prepare;

    /// <summary>
    /// Returns true if this dependency applies during the Analyse phase.
    /// </summary>
    public bool AppliesToAnalyse => Phase is DependencyPhase.Analyse;
}
