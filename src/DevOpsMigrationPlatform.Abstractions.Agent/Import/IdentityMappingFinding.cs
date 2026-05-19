// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

namespace DevOpsMigrationPlatform.Abstractions.Agent.Import;

public enum IdentityMappingFindingStatus
{
    Mapped = 0,
    Unmapped = 1,
    Error = 2
}

public enum IdentityMappingOperatorDecision
{
    Block = 0,
    UseDefault = 1,
    Skip = 2
}

public sealed record IdentityMappingFinding(
    string SourceId,
    string SourceDisplay,
    IdentityMappingFindingStatus Status,
    string? TargetId,
    IdentityMappingOperatorDecision? OperatorDecision);

