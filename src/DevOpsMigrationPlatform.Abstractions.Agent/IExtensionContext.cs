// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using DevOpsMigrationPlatform.Abstractions.Storage;

namespace DevOpsMigrationPlatform.Abstractions.Agent;

/// <summary>
/// Per-entity context passed to each <see cref="IModuleExtension"/> during export or import.
/// </summary>
/// <remarks>
/// <para>
/// The orchestrator creates one context per entity and passes it to every enabled extension
/// in <see cref="IModuleExtension.Order"/> order. Domain-specific orchestrators produce a
/// domain-specific sealed record that implements this interface; extensions cast to the
/// concrete type they require.
/// </para>
/// <para>
/// On export: <see cref="TargetEntityId"/> is null — it is not yet known.
/// On import: <see cref="TargetEntityId"/> is set by the orchestrator after the entity
/// is created or resolved on the target system, before extensions are invoked.
/// </para>
/// </remarks>
public interface IExtensionContext
{
    /// <summary>Organisation (account) of the source system.</summary>
    string Organisation { get; }

    /// <summary>Project name in the source (export) or target (import) system.</summary>
    string ProjectName { get; }

    /// <summary>Source entity identifier (e.g. team ID, work item ID).</summary>
    string EntityId { get; }

    /// <summary>
    /// Target entity identifier. Null during export; set by the orchestrator before
    /// extensions are invoked during import.
    /// </summary>
    string? TargetEntityId { get; }

    /// <summary>Package access for reading and writing artefact files.</summary>
    IPackageAccess Package { get; }
}
