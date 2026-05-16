// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

namespace DevOpsMigrationPlatform.Abstractions.Agent.Import;

public enum IdentityMappingFindingStatus
{
    Mapped = 0,
    Unresolved = 1,
    Error = 2
}

public sealed record IdentityMappingFinding(
    string SourceIdentityId,
    IdentityMappingFindingStatus Status,
    string? TargetReference,
    string? OperatorDecision);

