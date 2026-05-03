// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;

namespace DevOpsMigrationPlatform.Abstractions.Agent.Checkpointing;

public sealed record JobPhaseRecord
{
    public bool ExportCompleted { get; init; }
    public bool ImportCompleted { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
}
