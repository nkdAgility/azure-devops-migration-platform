// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

namespace DevOpsMigrationPlatform.Abstractions.Agent.Import;

public enum WorkItemTypeFindingStatus
{
    SupportedOnTarget = 0,
    UnsupportedOnTarget = 1,
    Error = 2
}

public sealed record WorkItemTypeFinding(
    string TypeName,
    int Count,
    WorkItemTypeFindingStatus Status,
    string? ErrorMessage);

