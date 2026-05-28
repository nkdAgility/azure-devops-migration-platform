// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

namespace DevOpsMigrationPlatform.Abstractions.Agent.WorkItems;

/// <summary>
/// Severity for a prepare-time import failure finding.
/// </summary>
public enum ImportFailureSeverity
{
    Warning = 0,
    Blocking = 1
}

