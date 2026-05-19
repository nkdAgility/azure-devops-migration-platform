// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

namespace DevOpsMigrationPlatform.Abstractions.Agent.Import;

/// <summary>
/// Aggregate readiness outcome for WorkItems prepare-time failure pattern evaluation.
/// </summary>
public enum WorkItemsPrepareReadinessResult
{
    Ready = 0,
    ChangesRequired = 1
}

