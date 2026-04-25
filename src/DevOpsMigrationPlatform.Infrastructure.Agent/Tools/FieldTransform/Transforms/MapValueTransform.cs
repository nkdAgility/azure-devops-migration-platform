using System;
using System.Collections.Generic;
using DevOpsMigrationPlatform.Abstractions.Agent.Tools;
using DevOpsMigrationPlatform.Abstractions.Options;
using Microsoft.Extensions.Logging;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Tools.FieldTransform.Transforms;

/// <summary>
/// Replaces a field value using a static lookup table.
/// Values not present in the map are preserved and a warning is emitted (FR-011).
/// </summary>
public sealed class MapValueTransform : IFieldTransform
{
    private readonly string _groupName;
    private readonly string _field;
    private readonly IReadOnlyDictionary<string, string> _valueMap;
    private readonly IReadOnlyList<string>? _applyTo;
    private readonly ILogger<MapValueTransform> _logger;

    public string Type => "MapValue";
    public string Name { get; }

    public MapValueTransform(
        string name,
        string groupName,
        string field,
        IReadOnlyDictionary<string, string> valueMap,
        IReadOnlyList<string>? applyTo,
        ILogger<MapValueTransform> logger)
    {
        Name = name;
        _groupName = groupName;
        _field = field;
        _valueMap = valueMap;
        _applyTo = applyTo;
        _logger = logger;
    }

    /// <inheritdoc />
    public FieldTransformResult Apply(
        IReadOnlyDictionary<string, object?> fields,
        FieldTransformContext context)
    {
        if (_applyTo != null && _applyTo.Count > 0)
        {
            bool matches = false;
            foreach (var t in _applyTo)
            {
                if (string.Equals(t, context.WorkItemType, StringComparison.OrdinalIgnoreCase))
                {
                    matches = true;
                    break;
                }
            }
            if (!matches)
                return new FieldTransformResult(fields, new List<FieldTransformAction>());
        }

        if (!fields.TryGetValue(_field, out var rawValue))
            return new FieldTransformResult(fields, new List<FieldTransformAction>());

        var oldValue = rawValue?.ToString() ?? string.Empty;

        var updated = new Dictionary<string, object?>(fields.Count);
        foreach (var kvp in fields)
            updated[kvp.Key] = kvp.Value;

        if (_valueMap.TryGetValue(oldValue, out var newValue))
        {
            updated[_field] = newValue;
            return new FieldTransformResult(
                updated,
                new List<FieldTransformAction>
                {
                    new FieldTransformAction(_groupName, Name, Type, _field, true, oldValue, newValue)
                });
        }

        // FR-011: preserve original and warn when no mapping exists
        _logger.LogWarning(
            "MapValueTransform '{Name}': value '{Value}' for field '{Field}' not found in value map. Preserving original.",
            Name, oldValue, _field);

        return new FieldTransformResult(
            updated,
            new List<FieldTransformAction>
            {
                new FieldTransformAction(_groupName, Name, Type, _field, false, oldValue, oldValue)
            });
    }
}
