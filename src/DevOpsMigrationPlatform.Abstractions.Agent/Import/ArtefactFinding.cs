// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

namespace DevOpsMigrationPlatform.Abstractions.Agent.Import;

public enum ArtefactFindingType
{
    RevisionFolder = 0,
    Attachment = 1,
    EmbeddedImage = 2
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

