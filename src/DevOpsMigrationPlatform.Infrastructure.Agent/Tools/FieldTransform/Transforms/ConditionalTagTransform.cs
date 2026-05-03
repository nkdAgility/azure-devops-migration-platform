// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using DevOpsMigrationPlatform.Abstractions.Agent.Tools;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Tools.FieldTransform.Transforms;

/// <summary>
/// Appends <c>_tag</c> to <c>System.Tags</c> when <c>_conditionField</c> matches
/// <c>_pattern</c> (FR-018: regex evaluated with a 1-second timeout to prevent ReDoS).
/// </summary>
public sealed class ConditionalTagTransform : IFieldTransform
{
    private const string TagsField = "System.Tags";

    private readonly string _conditionField;
    private readonly string _pattern;
    private readonly string _tag;
    private readonly string _groupName;

    public string Type => "ConditionalTag";
    public string Name { get; }

    public ConditionalTagTransform(
        string name,
        string groupName,
        string conditionField,
        string pattern,
        string tag)
    {
        Name = name;
        _groupName = groupName;
        _conditionField = conditionField;
        _pattern = pattern;
        _tag = tag;
    }

    /// <inheritdoc />
    public FieldTransformResult Apply(
        IReadOnlyDictionary<string, object?> fields,
        FieldTransformContext context)
    {
        if (!fields.TryGetValue(_conditionField, out var raw))
            return new FieldTransformResult(fields, new List<FieldTransformAction>());

        var value = raw?.ToString() ?? string.Empty;
        bool matches = Regex.IsMatch(value, _pattern, RegexOptions.None, TimeSpan.FromSeconds(1));

        if (!matches)
            return new FieldTransformResult(fields, new List<FieldTransformAction>());

        var updated = new Dictionary<string, object?>(fields.Count);
        foreach (var kvp in fields)
            updated[kvp.Key] = kvp.Value;

        var oldTags = updated.TryGetValue(TagsField, out var ot) ? ot?.ToString() : null;
        var newTags = WorkItemTagParser.AppendTag(oldTags, _tag);
        updated[TagsField] = newTags;

        return new FieldTransformResult(
            updated,
            new List<FieldTransformAction>
            {
                new FieldTransformAction(_groupName, Name, Type, TagsField, true, oldTags, newTags)
            });
    }
}
