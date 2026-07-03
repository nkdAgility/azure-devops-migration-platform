// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System.Collections.Generic;

namespace DevOpsMigrationPlatform.Abstractions.Agent.Modules;

/// <summary>
/// A single Selection aspect entry — what this module can bring into scope.
/// See .agents/10-contracts/specs/module-anatomy-contract.md.
/// </summary>
public interface ISelectionDefinition
{
    /// <summary>Entry name as it appears under the module's <c>Selection</c> config object.</summary>
    string Name { get; }

    /// <summary>Required entries cannot be disabled; optional entries may be enabled/disabled.</summary>
    bool Required { get; }
}

/// <summary>A single Data aspect entry — a canonical package payload kind for selected entities.</summary>
public interface IDataDefinition
{
    /// <summary>Entry name as it appears under the module's <c>Data</c> config object.</summary>
    string Name { get; }

    /// <summary>Required entries cannot be disabled; optional entries may be enabled/disabled.</summary>
    bool Required { get; }
}

/// <summary>A single Processing aspect entry — a runtime behaviour policy for export/import phases.</summary>
public interface IProcessingDefinition
{
    /// <summary>Entry name as it appears under the module's <c>Processing</c> config object.</summary>
    string Name { get; }

    /// <summary>Required entries cannot be disabled; optional entries may be enabled/disabled.</summary>
    bool Required { get; }
}

/// <summary>
/// Platform-owned, non-user-editable metadata describing a module's configuration anatomy:
/// exactly three aspects — Selection (what to migrate), Data (what to carry),
/// Processing (how to execute). Exposed via <see cref="IModule.Contract"/>.
/// </summary>
public interface IModuleContract
{
    /// <summary>Module name matching its <c>MigrationPlatform:Modules:{Name}</c> config section.</summary>
    string ModuleName { get; }

    /// <summary>In-scope entity selection entries.</summary>
    IReadOnlyList<ISelectionDefinition> Selection { get; }

    /// <summary>Canonical package payload entries for selected entities.</summary>
    IReadOnlyList<IDataDefinition> Data { get; }

    /// <summary>Runtime behaviour policy entries for export/import phases.</summary>
    IReadOnlyList<IProcessingDefinition> Processing { get; }
}

/// <summary>Immutable Selection entry.</summary>
public sealed record SelectionDefinition(string Name, bool Required) : ISelectionDefinition;

/// <summary>Immutable Data entry.</summary>
public sealed record DataDefinition(string Name, bool Required) : IDataDefinition;

/// <summary>Immutable Processing entry.</summary>
public sealed record ProcessingDefinition(string Name, bool Required) : IProcessingDefinition;

/// <summary>Immutable <see cref="IModuleContract"/> implementation used by all platform modules.</summary>
public sealed class ModuleContract(
    string moduleName,
    IReadOnlyList<ISelectionDefinition> selection,
    IReadOnlyList<IDataDefinition> data,
    IReadOnlyList<IProcessingDefinition> processing) : IModuleContract
{
    public string ModuleName { get; } = moduleName;
    public IReadOnlyList<ISelectionDefinition> Selection { get; } = selection;
    public IReadOnlyList<IDataDefinition> Data { get; } = data;
    public IReadOnlyList<IProcessingDefinition> Processing { get; } = processing;
}
