// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

namespace DevOpsMigrationPlatform.Abstractions.Storage;

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
    PrepareProbe,
    RunConfigSnapshot,
    ExportProgressDb,
    IdMapDb,
    WorkItemsImportReadiness,
    JobErrors
}

