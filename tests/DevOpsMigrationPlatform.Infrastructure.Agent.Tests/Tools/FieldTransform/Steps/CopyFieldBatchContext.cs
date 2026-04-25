using System.Collections.Generic;
using DevOpsMigrationPlatform.Abstractions.Agent.Tools;
using DevOpsMigrationPlatform.Infrastructure.Agent.Tools.FieldTransform.Transforms;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Tests.Tools.FieldTransform.Steps;

/// <summary>
/// Shared scenario state for Work Item Field Batch Copying step definitions.
/// </summary>
public class CopyFieldBatchContext
{
    public Dictionary<string, object?> InputFields { get; set; } = new Dictionary<string, object?>();
    public string WorkItemType { get; set; } = "Bug";
    public FieldTransformResult? Result { get; set; }

    private readonly Dictionary<string, string> _mappings = new Dictionary<string, string>();

    public void AddMapping(string sourceField, string targetField)
        => _mappings[sourceField] = targetField;

    public void Execute()
    {
        var transform = new CopyFieldBatchTransform(
            "BDD.CopyFieldBatch1",
            "BDD",
            _mappings);

        var context = new FieldTransformContext(1, 0, WorkItemType, FieldTransformPhase.Import);
        Result = transform.Apply(InputFields, context);
    }
}
