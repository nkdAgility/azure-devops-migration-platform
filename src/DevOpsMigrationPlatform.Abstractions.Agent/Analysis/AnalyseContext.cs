// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using DevOpsMigrationPlatform.Abstractions.Agent.Storage;
using DevOpsMigrationPlatform.Abstractions.Jobs;
using DevOpsMigrationPlatform.Abstractions.Streaming;

namespace DevOpsMigrationPlatform.Abstractions.Agent.Analysis;

public record AnalyseContext
{
    public required Job Job { get; init; }
    public required IArtefactStore ArtefactStore { get; init; }
    public required IStateStore StateStore { get; init; }
    public IProgressSink? ProgressSink { get; init; }
    public JobPolicies Policies { get; init; } = new();
}

