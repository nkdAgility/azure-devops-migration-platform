// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

namespace DevOpsMigrationPlatform.Abstractions.Agent.WorkItems;

/// <summary>
/// A link to an external artefact (e.g. a build, test result, or commit).
/// Stored inside the ExternalLinks collection of a <see cref="WorkItemRevision"/>.
/// </summary>
public record ExternalWorkItemLink
{
    /// <summary>The link-type name, e.g. "Build".</summary>
    public string ArtifactLinkType { get; init; } = string.Empty;

    /// <summary>Optional comment recorded on the link.</summary>
    public string? Comment { get; init; }

    /// <summary>The URI of the linked external artefact.</summary>
    public string LinkedArtifactUri { get; init; } = string.Empty;
}
