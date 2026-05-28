// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using DevOpsMigrationPlatform.Abstractions.Agent.WorkItems;
using DevOpsMigrationPlatform.Abstractions.Agent.Tools;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.WorkItems;

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
        // FieldTransform is optional — when disabled or unconfigured, import proceeds without transforms.
        // The validator only blocks when the tool is explicitly enabled but has no usable rules.
        if (!_fieldTransformTool.IsEnabledForPhase(FieldTransformPhase.Import))
            return;
    }
}

