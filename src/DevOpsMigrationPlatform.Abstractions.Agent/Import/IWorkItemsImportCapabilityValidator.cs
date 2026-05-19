// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

namespace DevOpsMigrationPlatform.Abstractions.Agent.Import;

/// <summary>
/// Validates preconditions for WorkItems import capability.
/// </summary>
public interface IWorkItemsImportCapabilityValidator
{
    /// <summary>
    /// Validates the current runtime and configuration support WorkItems import.
    /// </summary>
    void Validate();
}

