// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

namespace DevOpsMigrationPlatform.Abstractions.Agent.Storage;

public enum PackageMetaKind
{
    MigrationConfig,
    JobDescriptor,
    ExecutionPlan,
    PhaseRecord,
    CheckpointCursor,
    ContinuationToken,
    InventoryCompletionMarker,
    PrepareReport,
    RunConfigSnapshot,
    ExportProgressDb,
    IdMapDb
}

