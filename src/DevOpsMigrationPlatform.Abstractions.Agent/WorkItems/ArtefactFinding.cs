// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

namespace DevOpsMigrationPlatform.Abstractions.Agent.WorkItems;

public enum ArtefactFindingType
{
    RevisionFolder = 0,
    Attachment = 1,
    EmbeddedImage = 2,

    /// <summary>
    /// A module-level package artefact (e.g. <c>Nodes/source-tree.json</c>,
    /// <c>Teams/{slug}/team.json</c>) validated during the Prepare phase (ADR-0027, MC-L1).
    /// </summary>
    ModuleArtefact = 3
}

public enum ArtefactFindingStatus
{
    Missing = 0,
    Invalid = 1
}

public sealed record ArtefactFinding(
    ArtefactFindingType ItemType,
    string ItemId,
    ArtefactFindingStatus Status,
    string MissingPath);

