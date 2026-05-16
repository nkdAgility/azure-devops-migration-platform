// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

namespace DevOpsMigrationPlatform.Abstractions.Agent.Import;

public enum WorkItemTypeFindingStatus
{
    Found = 0,
    Missing = 1,
    Error = 2
}

public sealed record WorkItemTypeFinding(
    string TypeName,
    WorkItemTypeFindingStatus Status,
    string? TargetReference);

