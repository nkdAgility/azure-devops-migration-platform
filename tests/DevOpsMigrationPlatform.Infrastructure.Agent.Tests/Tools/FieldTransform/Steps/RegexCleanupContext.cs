using System.Collections.Generic;
using DevOpsMigrationPlatform.Abstractions.Agent.Tools;
using DevOpsMigrationPlatform.Infrastructure.Agent.Tools.FieldTransform.Transforms;
using Microsoft.Extensions.Logging.Abstractions;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Tests.Tools.FieldTransform.Steps;

/// <summary>
/// Shared scenario state for Work Item Field Regex Cleanup step definitions.
/// </summary>
public class RegexCleanupContext
{
    public List<IFieldTransform> Transforms { get; } = new List<IFieldTransform>();
    public Dictionary<string, object?> InputFields { get; set; } = new Dictionary<string, object?>();
    public FieldTransformResult? Result { get; set; }

    public void AddRegexFieldTransform(string field, string pattern, string replacement, string groupName = "BDD")
    {
        var ordinal = Transforms.Count + 1;
        Transforms.Add(new RegexFieldTransform(
            $"{groupName}.RegexField{ordinal}",
            groupName,
            field,
            pattern,
            replacement,
            NullLogger<RegexFieldTransform>.Instance));
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
