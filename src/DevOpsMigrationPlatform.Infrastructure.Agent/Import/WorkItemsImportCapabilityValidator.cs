// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using DevOpsMigrationPlatform.Abstractions.Agent.Import;
using DevOpsMigrationPlatform.Abstractions.Agent.Tools;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Import;

/// <summary>
/// Canonical WorkItems import capability validator.
/// </summary>
public sealed class WorkItemsImportCapabilityValidator : IWorkItemsImportCapabilityValidator
{
    private readonly IFieldTransformTool _fieldTransformTool;

    public WorkItemsImportCapabilityValidator(IFieldTransformTool fieldTransformTool)
    {
        _fieldTransformTool = fieldTransformTool ?? throw new ArgumentNullException(nameof(fieldTransformTool));
    }

    public void Validate()
    {
        if (!_fieldTransformTool.IsEnabledForPhase(FieldTransformPhase.Import))
            throw new InvalidOperationException(
                "FieldTransform is not configured for Import phase. Configure MigrationPlatform:Tools:FieldTransform with at least one enabled Import/Both transform rule before running WorkItems import.");
    }
}

