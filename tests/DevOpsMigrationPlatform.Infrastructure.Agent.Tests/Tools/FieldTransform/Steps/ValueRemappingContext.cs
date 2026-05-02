// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) NKD Agility Limited

using System.Collections.Generic;
using DevOpsMigrationPlatform.Abstractions.Agent.Tools;
using DevOpsMigrationPlatform.Infrastructure.Agent.Tools.FieldTransform.Transforms;
using Microsoft.Extensions.Logging.Abstractions;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Tests.Tools.FieldTransform.Steps;

/// <summary>
/// Shared scenario state for Work Item Field Value Remapping step definitions.
/// </summary>
public class ValueRemappingContext
{
    /// <summary>Transforms to apply in declaration order.</summary>
    public List<MapValueTransform> Transforms { get; } = new List<MapValueTransform>();

    /// <summary>Input fields for the current scenario.</summary>
    public Dictionary<string, object?> InputFields { get; set; } = new Dictionary<string, object?>();

    /// <summary>Work item type for the current scenario.</summary>
    public string WorkItemType { get; set; } = "Bug";

    /// <summary>Result after executing all transforms in order.</summary>
    public FieldTransformResult? Result { get; set; }

    /// <summary>
    /// Appends a <see cref="MapValueTransform"/> to the pipeline.
    /// </summary>
    public void AddTransform(
        string field,
        IReadOnlyDictionary<string, string> valueMap,
        IReadOnlyList<string>? applyTo = null,
        string groupName = "BDD",
        string? name = null)
    {
        var ordinal = Transforms.Count + 1;
        var transformName = name ?? $"{groupName}.MapValue{ordinal}";
        Transforms.Add(new MapValueTransform(
            transformName,
            groupName,
            field,
            valueMap,
            applyTo,
            NullLogger<MapValueTransform>.Instance));
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
