// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using DevOpsMigrationPlatform.Abstractions.Storage;
using DevOpsMigrationPlatform.Abstractions.Streaming;

namespace DevOpsMigrationPlatform.Abstractions.Agent.Teams;

/// <summary>
/// Per-team context passed to each <see cref="IModuleExtension"/> during Teams module
/// export or import. Extensions cast <see cref="IExtensionContext"/> to this type.
/// </summary>
/// <remarks>
/// <para>
/// On export: <see cref="TargetEntityId"/> is null — the target team is not yet known.
/// On import: <see cref="TargetEntityId"/> is the resolved target team ID, set by
/// <c>TeamsOrchestrator</c> after <c>CreateOrUpdateTeamAsync</c> completes, before
/// any extension is invoked.
/// </para>
/// <para>
/// This record carries no shared module-options object. Each extension owns its own
/// <c>IOptions&lt;T&gt;</c> and reads its settings from there.
/// </para>
/// </remarks>
public sealed record TeamExtensionContext : IExtensionContext
{
    /// <inheritdoc/>
    public required string Organisation { get; init; }

    /// <inheritdoc/>
    public required string ProjectName { get; init; }

    /// <inheritdoc/>
    /// <remarks>The source team ID (<see cref="TeamDefinition.Id"/>).</remarks>
    public required string EntityId { get; init; }

    /// <inheritdoc/>
    /// <remarks>
    /// Null during export. Set to the created/resolved target team ID by the orchestrator
    /// before import extensions are invoked.
    /// </remarks>
    public string? TargetEntityId { get; init; }

    /// <inheritdoc/>
    public required IPackageAccess Package { get; init; }

    /// <summary>Full team definition from the source system.</summary>
    public required TeamDefinition Team { get; init; }

    /// <summary>
    /// URL-safe slug derived from the team name, used as the folder name under
    /// <c>Teams/{slug}/</c> in the package.
    /// </summary>
    public required string Slug { get; init; }

    /// <summary>Source project name. Used for path translation during import.</summary>
    public required string SourceProjectName { get; init; }

    /// <summary>Progress event sink. Null in unit-test contexts where no sink is wired.</summary>
    public IProgressSink? ProgressSink { get; init; }
}
