// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) NKD Agility Limited

using System.Collections.Generic;
using DevOpsMigrationPlatform.Abstractions.Agent.Tools;
using DevOpsMigrationPlatform.Infrastructure.Agent.Tools.FieldTransform.Transforms;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Tests.Tools.FieldTransform.Steps;

/// <summary>
/// Shared scenario state for Work Item Field Copying and Renaming step definitions.
/// </summary>
public class CopyFieldContext
{
    /// <summary>Transforms to apply in declaration order.</summary>
    public List<IFieldTransform> Transforms { get; } = new List<IFieldTransform>();

    /// <summary>Input fields for the current scenario.</summary>
    public Dictionary<string, object?> InputFields { get; set; } = new Dictionary<string, object?>();

    /// <summary>Work item type for the current scenario.</summary>
    public string WorkItemType { get; set; } = "Bug";

    /// <summary>Result after executing all transforms in order.</summary>
    public FieldTransformResult? Result { get; set; }

    /// <summary>
    /// Appends a <see cref="CopyFieldTransform"/> to the pipeline.
    /// </summary>
    public void AddCopyFieldTransform(
        string sourceField,
        string targetField,
        string? defaultValue = null,
        string groupName = "BDD")
    {
        var ordinal = Transforms.Count + 1;
        Transforms.Add(new CopyFieldTransform(
            $"{groupName}.CopyField{ordinal}",
            groupName,
            sourceField,
            targetField,
            defaultValue));
    }

    /// <summary>
    /// Applies all configured transforms sequentially, feeding each output into the next.
    /// </summary>
    public void Execute()
    {
        IReadOnlyDictionary<string, object?> current = InputFields;
        var allActions = new List<FieldTransformAction>();
        var context = new FieldTransformContext(1, 0, WorkItemType, FieldTransformPhase.Import);

        foreach (var transform in Transforms)
        {
            var stepResult = transform.Apply(current, context);
            current = stepResult.Fields;
            allActions.AddRange(stepResult.Actions);
        }

        Result = new FieldTransformResult(current, allActions);
    }
}
