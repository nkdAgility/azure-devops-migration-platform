// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

namespace DevOpsMigrationPlatform.Abstractions.Agent.Import;

public enum NodeReadinessNodeType
{
    Area = 0,
    Iteration = 1
}

public enum NodeReadinessFindingStatus
{
    ReferencedInPackage = 0,
    ExistsOnTarget = 1,
    WillBeCreated = 2,
    Missing = 3,
    TranslationError = 4
}

public sealed record NodeReadinessFinding(
    string Path,
    NodeReadinessNodeType NodeType,
    NodeReadinessFindingStatus Status,
    string? TargetPath);
