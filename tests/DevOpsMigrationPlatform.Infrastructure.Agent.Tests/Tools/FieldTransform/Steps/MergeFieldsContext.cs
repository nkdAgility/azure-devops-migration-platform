// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System.Collections.Generic;
using DevOpsMigrationPlatform.Abstractions.Agent.Tools;
using DevOpsMigrationPlatform.Infrastructure.Agent.Tools.FieldTransform.Transforms;
using Microsoft.Extensions.Logging.Abstractions;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Tests.Tools.FieldTransform.Steps;

/// <summary>
/// Shared scenario state for Work Item Field Merging and Conditional Assignment step definitions.
/// </summary>
public class MergeFieldsContext
{
    public List<IFieldTransform> Transforms { get; } = new List<IFieldTransform>();
    public Dictionary<string, object?> InputFields { get; set; } = new Dictionary<string, object?>();
    public FieldTransformResult? Result { get; set; }

    public void AddMergeFieldsTransform(
        IReadOnlyList<string> sourceFields,
        string targetField,
        string formatString,
        string groupName = "BDD")
    {
        var ordinal = Transforms.Count + 1;
        Transforms.Add(new MergeFieldsTransform(
            $"{groupName}.MergeFields{ordinal}",
            groupName,
            sourceFields,
            targetField,
            formatString));
    }

    public void AddConditionalFieldTransform(
        string conditionField,
        string condition,
        string targetField,
        string? trueValue,
        string? falseValue,
        string groupName = "BDD")
    {
        var ordinal = Transforms.Count + 1;
        Transforms.Add(new ConditionalFieldTransform(
            $"{groupName}.ConditionalField{ordinal}",
            groupName,
            conditionField,
            condition,
            targetField,
            trueValue,
            falseValue,
            NullLogger<ConditionalFieldTransform>.Instance));
    }

    public void Execute()
    {
        IReadOnlyDictionary<string, object?> current = InputFields;
        var allActions = new List<FieldTransformAction>();
        var context = new FieldTransformContext(1, 0, "Bug", FieldTransformPhase.Import);

        foreach (var transform in Transforms)
        {
            var stepResult = transform.Apply(current, context);
            current = stepResult.Fields;
            allActions.AddRange(stepResult.Actions);
        }

        Result = new FieldTransformResult(current, allActions);
    }
}
