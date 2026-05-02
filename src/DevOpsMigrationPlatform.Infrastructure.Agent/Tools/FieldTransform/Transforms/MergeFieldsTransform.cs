// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System.Collections.Generic;
using DevOpsMigrationPlatform.Abstractions.Agent.Tools;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Tools.FieldTransform.Transforms;

/// <summary>
/// Merges values from multiple source fields into a single target field
/// using a <c>string.Format</c> format string where <c>{0}</c>, <c>{1}</c>, …
/// correspond to the ordered source fields. Absent fields are treated as empty string.
/// </summary>
public sealed class MergeFieldsTransform : IFieldTransform
{
    private readonly IReadOnlyList<string> _sourceFields;
    private readonly string _targetField;
    private readonly string _formatString;
    private readonly string _groupName;

    public string Type => "MergeFields";
    public string Name { get; }

    public MergeFieldsTransform(
        string name,
        string groupName,
        IReadOnlyList<string> sourceFields,
        string targetField,
        string formatString)
    {
        Name = name;
        _groupName = groupName;
        _sourceFields = sourceFields;
        _targetField = targetField;
        _formatString = formatString;
    }

    /// <inheritdoc />
    public FieldTransformResult Apply(
        IReadOnlyDictionary<string, object?> fields,
        FieldTransformContext context)
    {
        var values = new object[_sourceFields.Count];
        for (int i = 0; i < _sourceFields.Count; i++)
        {
            values[i] = fields.TryGetValue(_sourceFields[i], out var v)
                ? (v?.ToString() ?? string.Empty)
                : string.Empty;
        }

        var merged = string.Format(_formatString, values);

        var updated = new Dictionary<string, object?>(fields.Count + 1);
        foreach (var kvp in fields)
            updated[kvp.Key] = kvp.Value;

        var oldValue = updated.TryGetValue(_targetField, out var ov) ? ov?.ToString() : null;
        updated[_targetField] = merged;

        return new FieldTransformResult(updated,
            new List<FieldTransformAction>
            {
                new FieldTransformAction(_groupName, Name, Type, _targetField, true, oldValue, merged)
            });
    }
}
