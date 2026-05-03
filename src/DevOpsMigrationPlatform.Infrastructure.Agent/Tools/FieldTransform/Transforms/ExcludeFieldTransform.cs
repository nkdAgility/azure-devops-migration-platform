// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System.Collections.Generic;
using DevOpsMigrationPlatform.Abstractions.Agent.Tools;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Tools.FieldTransform.Transforms;

/// <summary>
/// Removes a field from the work item dictionary entirely.
/// If the field is absent, the transform is a silent no-op (FR-014).
/// </summary>
public sealed class ExcludeFieldTransform : IFieldTransform
{
    private readonly string _field;
    private readonly string _groupName;

    public string Type => "ExcludeField";
    public string Name { get; }

    public ExcludeFieldTransform(string name, string groupName, string field)
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
        if (!fields.ContainsKey(_field))
            return new FieldTransformResult(fields, new List<FieldTransformAction>());

        var updated = new Dictionary<string, object?>(fields.Count);
        foreach (var kvp in fields)
            updated[kvp.Key] = kvp.Value;

        var oldValue = updated[_field]?.ToString();
        updated.Remove(_field);

        return new FieldTransformResult(
            updated,
            new List<FieldTransformAction>
            {
                new FieldTransformAction(_groupName, Name, Type, _field, true, oldValue, null)
            });
    }
}
