using System.Collections.Generic;
using DevOpsMigrationPlatform.Abstractions.Agent.Tools;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Tools.FieldTransform.Transforms;

/// <summary>
/// Merges tag values from one or more <c>_sourceFields</c> into <c>System.Tags</c>,
/// deduplicating the result case-insensitively.
/// </summary>
public sealed class MergeToTagTransform : IFieldTransform
{
    private const string TagsField = "System.Tags";

    private readonly IReadOnlyList<string> _sourceFields;
    private readonly string _groupName;

    public string Type => "MergeToTag";
    public string Name { get; }

    public MergeToTagTransform(string name, string groupName, IReadOnlyList<string> sourceFields)
    {
        Name = name;
        _groupName = groupName;
        _sourceFields = sourceFields;
    }

    /// <inheritdoc />
    public FieldTransformResult Apply(
        IReadOnlyDictionary<string, object?> fields,
        FieldTransformContext context)
    {
        var updated = new Dictionary<string, object?>(fields.Count);
        foreach (var kvp in fields)
            updated[kvp.Key] = kvp.Value;

        var oldTags = updated.TryGetValue(TagsField, out var ot) ? ot?.ToString() : null;
        var combined = oldTags ?? string.Empty;

        foreach (var sourceField in _sourceFields)
        {
            if (!fields.TryGetValue(sourceField, out var raw) || raw == null)
                continue;
            var value = raw.ToString() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(value))
                continue;

            // Only merge from non-tags source fields into the combined string
            // For the tags field itself, the value is already in combined
            if (!string.Equals(sourceField, TagsField, System.StringComparison.OrdinalIgnoreCase))
                combined = TagUtilities.AppendTag(combined, value);
        }

        var newTags = TagUtilities.Deduplicate(combined);
        updated[TagsField] = newTags;

        return new FieldTransformResult(
            updated,
            new List<FieldTransformAction>
            {
                new FieldTransformAction(_groupName, Name, Type, TagsField, true, oldTags, newTags)
            });
    }
}
