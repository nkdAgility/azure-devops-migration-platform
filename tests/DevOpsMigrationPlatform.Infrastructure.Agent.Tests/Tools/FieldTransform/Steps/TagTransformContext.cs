using System.Collections.Generic;
using DevOpsMigrationPlatform.Abstractions.Agent.Tools;
using DevOpsMigrationPlatform.Infrastructure.Agent.Tools.FieldTransform.Transforms;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Tests.Tools.FieldTransform.Steps;

/// <summary>
/// Shared scenario state for Work Item Field to Tag Transform step definitions.
/// </summary>
public class TagTransformContext
{
    public List<IFieldTransform> Transforms { get; } = new List<IFieldTransform>();
    public Dictionary<string, object?> InputFields { get; set; } = new Dictionary<string, object?>();
    public string WorkItemType { get; set; } = "Bug";
    public FieldTransformResult? Result { get; set; }

    public void AddFieldToTagTransform(string sourceField, string groupName = "BDD")
    {
        var ordinal = Transforms.Count + 1;
        Transforms.Add(new FieldToTagTransform(
            $"{groupName}.FieldToTag{ordinal}", groupName, sourceField));
    }

    public void AddConditionalTagTransform(
        string conditionField, string pattern, string tag, string groupName = "BDD")
    {
        var ordinal = Transforms.Count + 1;
        Transforms.Add(new ConditionalTagTransform(
            $"{groupName}.ConditionalTag{ordinal}", groupName, conditionField, pattern, tag));
    }

    public void AddMergeToTagTransform(IReadOnlyList<string> sourceFields, string groupName = "BDD")
    {
        var ordinal = Transforms.Count + 1;
        Transforms.Add(new MergeToTagTransform(
            $"{groupName}.MergeToTag{ordinal}", groupName, sourceFields));
    }

    public void AddTreeToTagTransform(string field, string groupName = "BDD")
    {
        var ordinal = Transforms.Count + 1;
        Transforms.Add(new TreeToTagTransform(
            $"{groupName}.TreeToTag{ordinal}", groupName, field));
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
