// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System.Collections.Generic;
using DevOpsMigrationPlatform.Abstractions.Agent.Tools;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Tools.FieldTransform.Transforms;

/// <summary>
/// Copies the value of one field to another, optionally using a default when the source is absent (FR-010).
/// If the source field is present but empty, the empty string is copied — the default is never used.
/// The target field is overwritten unconditionally when a value is written.
/// </summary>
public sealed class CopyFieldTransform : IFieldTransform
{
    private readonly string _sourceField;
    private readonly string _targetField;
    private readonly string? _defaultValue;
    private readonly string _groupName;

    public string Type => "CopyField";
    public string Name { get; }

    public CopyFieldTransform(
        string name,
        string groupName,
        string sourceField,
        string targetField,
        string? defaultValue)
    {
        Name = name;
        _groupName = groupName;
        _sourceField = sourceField;
        _targetField = targetField;
        _defaultValue = defaultValue;
    }

    /// <inheritdoc />
    public FieldTransformResult Apply(
        IReadOnlyDictionary<string, object?> fields,
        FieldTransformContext context)
    {
        var updated = new Dictionary<string, object?>(fields.Count);
        foreach (var kvp in fields)
            updated[kvp.Key] = kvp.Value;
        var actions = new List<FieldTransformAction>();

        bool sourceExists = fields.TryGetValue(_sourceField, out var sourceValue);

        if (!sourceExists)
        {
            if (_defaultValue != null)
            {
                var oldTarget = updated.TryGetValue(_targetField, out var ot) ? ot?.ToString() : null;
                updated[_targetField] = _defaultValue;
                actions.Add(new FieldTransformAction(_groupName, Name, Type, _targetField, true, oldTarget, _defaultValue));
            }
            // else: source absent and no default — no-op
        }
        else
        {
            var oldTarget = updated.TryGetValue(_targetField, out var ot) ? ot?.ToString() : null;
            updated[_targetField] = sourceValue;
            actions.Add(new FieldTransformAction(_groupName, Name, Type, _targetField, true, oldTarget, sourceValue?.ToString()));
        }

        return new FieldTransformResult(updated, actions);
    }
}
