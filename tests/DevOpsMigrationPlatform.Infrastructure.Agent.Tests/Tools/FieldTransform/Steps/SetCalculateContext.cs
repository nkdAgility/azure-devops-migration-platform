// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System.Collections.Generic;
using DevOpsMigrationPlatform.Abstractions.Agent.Tools;
using DevOpsMigrationPlatform.Infrastructure.Agent.Tools.FieldTransform.Transforms;
using Microsoft.Extensions.Logging.Abstractions;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Tests.Tools.FieldTransform.Steps;

/// <summary>
/// Shared scenario state for Work Item Field Value Assignment and Calculation step definitions.
/// </summary>
public class SetCalculateContext
{
    public List<IFieldTransform> Transforms { get; } = new List<IFieldTransform>();
    public Dictionary<string, object?> InputFields { get; set; } = new Dictionary<string, object?>();
    public string WorkItemType { get; set; } = "Bug";
    public FieldTransformResult? Result { get; set; }

    public void AddSetFieldTransform(string field, string value, string groupName = "BDD")
    {
        var ordinal = Transforms.Count + 1;
        Transforms.Add(new SetFieldTransform(
            $"{groupName}.SetField{ordinal}", groupName, field, value));
    }

    public void AddCalculateFieldTransform(string field, string expression, string groupName = "BDD")
    {
        var ordinal = Transforms.Count + 1;
        Transforms.Add(new CalculateFieldTransform(
            $"{groupName}.CalculateField{ordinal}",
            groupName,
            field,
            expression,
            new SimpleExpressionEvaluator(),
            NullLogger<CalculateFieldTransform>.Instance));
    }

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
