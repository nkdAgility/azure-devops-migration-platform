// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using DevOpsMigrationPlatform.Abstractions.Agent.Analysis;

namespace DevOpsMigrationPlatform.Abstractions.Agent.Modules;

/// <summary>
/// Represents a typed dependency between modules, specifying the target module
/// and the phase(s) during which the dependency applies.
/// </summary>
/// <remarks>
/// Targets are constrained by phase (ADR-0027, MC-L2):
/// <list type="bullet">
/// <item>Module phases (<see cref="DependencyPhase.Inventory"/>, <see cref="DependencyPhase.Export"/>,
/// <see cref="DependencyPhase.Import"/>, <see cref="DependencyPhase.Both"/>,
/// <see cref="DependencyPhase.Prepare"/>) must target <see cref="IModule"/> implementations.</item>
/// <item><see cref="DependencyPhase.Analyse"/> is the dedicated analyser-ordering mechanism
/// and must target <see cref="IAnalyser"/> implementations.</item>
/// </list>
/// </remarks>
public sealed record ModuleDependency
{
    /// <summary>
    /// Creates a typed dependency.
    /// </summary>
    /// <param name="ModuleType">The module (or, for <see cref="DependencyPhase.Analyse"/>, analyser) type that this module depends on.</param>
    /// <param name="Phase">The phase(s) during which this dependency applies.</param>
    /// <exception cref="ArgumentException">Thrown when the target type does not satisfy the phase's contract.</exception>
    public ModuleDependency(Type ModuleType, DependencyPhase Phase)
    {
        if (ModuleType is null)
            throw new ArgumentNullException(nameof(ModuleType));

        if (Phase == DependencyPhase.Analyse)
        {
            if (!typeof(IAnalyser).IsAssignableFrom(ModuleType))
                throw new ArgumentException(
                    $"Analyse-phase dependencies must target IAnalyser implementations, but '{ModuleType.FullName}' does not implement IAnalyser. " +
                    "Module ordering belongs in module-phase dependencies; analyser ordering is expressed through DependencyPhase.Analyse (ADR-0027).",
                    nameof(ModuleType));
        }
        else if (!typeof(IModule).IsAssignableFrom(ModuleType))
        {
            throw new ArgumentException(
                $"Module-phase dependencies must target IModule implementations, but '{ModuleType.FullName}' does not implement IModule. " +
                "To depend on an analyser, use DependencyPhase.Analyse (ADR-0027).",
                nameof(ModuleType));
        }

        this.ModuleType = ModuleType;
        this.Phase = Phase;
    }

    /// <summary>The module (or analyser, for Analyse-phase dependencies) type that this module depends on.</summary>
    public Type ModuleType { get; init; }

    /// <summary>The phase(s) during which this dependency applies.</summary>
    public DependencyPhase Phase { get; init; }

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

    /// <summary>Deconstructs the dependency into its target type and phase.</summary>
    public void Deconstruct(out Type ModuleType, out DependencyPhase Phase)
    {
        ModuleType = this.ModuleType;
        Phase = this.Phase;
    }
}
