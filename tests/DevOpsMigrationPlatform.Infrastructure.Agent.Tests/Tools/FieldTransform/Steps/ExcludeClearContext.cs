// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System.Collections.Generic;
using DevOpsMigrationPlatform.Abstractions.Agent.Tools;
using DevOpsMigrationPlatform.Infrastructure.Agent.Tools.FieldTransform.Transforms;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Tests.Tools.FieldTransform.Steps;

/// <summary>
/// Shared scenario state for Work Item Field Exclusion and Clearing step definitions.
/// </summary>
public class ExcludeClearContext
{
    public List<IFieldTransform> Transforms { get; } = new List<IFieldTransform>();
    public Dictionary<string, object?> InputFields { get; set; } = new Dictionary<string, object?>();
    public string WorkItemType { get; set; } = "Bug";
    public FieldTransformResult? Result { get; set; }
    public System.Exception? ExceptionCaught { get; set; }

    public void AddExcludeFieldTransform(string field, string groupName = "BDD")
    {
        var ordinal = Transforms.Count + 1;
        Transforms.Add(new ExcludeFieldTransform(
            $"{groupName}.ExcludeField{ordinal}", groupName, field));
    }

    public void AddClearFieldTransform(string field, string groupName = "BDD")
    {
        var ordinal = Transforms.Count + 1;
        Transforms.Add(new ClearFieldTransform(
            $"{groupName}.ClearField{ordinal}", groupName, field));
    }

    public void Execute()
    {
        try
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
        catch (System.Exception ex)
        {
            ExceptionCaught = ex;
        }
    }
}
