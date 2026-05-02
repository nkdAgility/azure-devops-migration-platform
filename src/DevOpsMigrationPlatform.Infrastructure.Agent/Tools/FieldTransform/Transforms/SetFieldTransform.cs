// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Collections.Generic;
using DevOpsMigrationPlatform.Abstractions.Agent.Tools;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Tools.FieldTransform.Transforms;

/// <summary>
/// Sets a field to a literal value.
/// Supports the <c>${migration.timestamp}</c> token which is replaced with the
/// current UTC timestamp in ISO-8601 round-trip format.
/// </summary>
public sealed class SetFieldTransform : IFieldTransform
{
    private readonly string _field;
    private readonly string? _value;
    private readonly string _groupName;

    public string Type => "SetField";
    public string Name { get; }

    public SetFieldTransform(string name, string groupName, string field, string? value)
    {
        Name = name;
        _groupName = groupName;
        _field = field;
        _value = value;
    }

    /// <inheritdoc />
    public FieldTransformResult Apply(
        IReadOnlyDictionary<string, object?> fields,
        FieldTransformContext context)
    {
        var value = _value?.Replace("${migration.timestamp}", DateTimeOffset.UtcNow.ToString("o"))
                    ?? string.Empty;

        var updated = new Dictionary<string, object?>(fields.Count);
        foreach (var kvp in fields)
            updated[kvp.Key] = kvp.Value;

        var oldValue = updated.TryGetValue(_field, out var ov) ? ov?.ToString() : null;
        updated[_field] = value;

        return new FieldTransformResult(
            updated,
            new List<FieldTransformAction>
            {
                new FieldTransformAction(_groupName, Name, Type, _field, true, oldValue, value)
            });
    }
}
