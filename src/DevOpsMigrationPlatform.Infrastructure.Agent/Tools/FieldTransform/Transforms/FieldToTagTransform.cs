using System.Collections.Generic;
using DevOpsMigrationPlatform.Abstractions.Agent.Tools;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Tools.FieldTransform.Transforms;

/// <summary>
/// Reads the value of <c>_sourceField</c> and appends it as a tag to
/// <c>System.Tags</c>.  If the source field is absent the transform is a no-op.
/// </summary>
public sealed class FieldToTagTransform : IFieldTransform
{
    private const string TagsField = "System.Tags";

    private readonly string _sourceField;
    private readonly string _groupName;

    public string Type => "FieldToTag";
    public string Name { get; }

    public FieldToTagTransform(string name, string groupName, string sourceField)
    {
        Name = name;
        _groupName = groupName;
        _sourceField = sourceField;
    }

    /// <inheritdoc />
    public FieldTransformResult Apply(
        IReadOnlyDictionary<string, object?> fields,
        FieldTransformContext context)
    {
        if (!fields.TryGetValue(_sourceField, out var raw) || raw == null)
            return new FieldTransformResult(fields, new List<FieldTransformAction>());

        var newTag = raw.ToString() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(newTag))
            return new FieldTransformResult(fields, new List<FieldTransformAction>());

        var updated = new Dictionary<string, object?>(fields.Count);
        foreach (var kvp in fields)
            updated[kvp.Key] = kvp.Value;

        var oldTags = updated.TryGetValue(TagsField, out var ot) ? ot?.ToString() : null;
        var newTags = WorkItemTagParser.AppendTag(oldTags, newTag);
        updated[TagsField] = newTags;

        return new FieldTransformResult(
            updated,
            new List<FieldTransformAction>
            {
                new FieldTransformAction(_groupName, Name, Type, TagsField, true, oldTags, newTags)
            });
    }
}
