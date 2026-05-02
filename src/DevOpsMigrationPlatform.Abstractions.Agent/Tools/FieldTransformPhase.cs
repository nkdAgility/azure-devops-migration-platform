// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

namespace DevOpsMigrationPlatform.Abstractions.Agent.Tools;

/// <summary>Phase during which a field transform is applied.</summary>
public enum FieldTransformPhase
{
    Export,
    Import,
    /// <summary>Future: in-place update of already-imported work items without a full re-migration.</summary>
    Update,
    Both
}
