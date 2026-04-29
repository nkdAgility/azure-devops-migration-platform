using System;
using System.Collections.Generic;
using DevOpsMigrationPlatform.Abstractions.Agent.Tools;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Tools.FieldTransform.Transforms;

/// <summary>
/// Splits <c>_field</c> value by the backslash path separator and appends each
/// non-empty segment as a tag to <c>System.Tags</c>.
/// Intended for Azure DevOps area and iteration path fields.
/// </summary>
public sealed class TreeToTagTransform : IFieldTransform
{
    private const string TagsField = "System.Tags";

    private readonly string _field;
    private readonly string _groupName;

    public string Type => "TreeToTag";
    public string Name { get; }

    public TreeToTagTransform(string name, string groupName, string field)
    {
        Name = name;
        _groupName = groupName;
        _field = field;
    }

    /// <inheritdoc />
    public FieldTransformResult Apply(
        IReadOnlyDictionary<string, object?> fields,
        FieldTransformContext context)
    {
        if (!fields.TryGetValue(_field, out var raw) || raw == null)
            return new FieldTransformResult(fields, new List<FieldTransformAction>());

        var path = raw.ToString() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(path))
            return new FieldTransformResult(fields, new List<FieldTransformAction>());

        var segments = path.Split(new[] { '\\' }, StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0)
            return new FieldTransformResult(fields, new List<FieldTransformAction>());

        var updated = new Dictionary<string, object?>(fields.Count);
        foreach (var kvp in fields)
            updated[kvp.Key] = kvp.Value;

        var oldTags = updated.TryGetValue(TagsField, out var ot) ? ot?.ToString() : null;
        var currentTags = oldTags;

        foreach (var segment in segments)
        {
            var trimmed = segment.Trim();
            if (!string.IsNullOrEmpty(trimmed))
                currentTags = WorkItemTagParser.AppendTag(currentTags, trimmed);
        }

        updated[TagsField] = currentTags;

        return new FieldTransformResult(
            updated,
            new List<FieldTransformAction>
            {
                new FieldTransformAction(_groupName, Name, Type, TagsField, true, oldTags, currentTags)
            });
    }
}
