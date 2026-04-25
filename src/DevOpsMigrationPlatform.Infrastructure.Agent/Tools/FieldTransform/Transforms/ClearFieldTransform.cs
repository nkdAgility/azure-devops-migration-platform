using System.Collections.Generic;
using DevOpsMigrationPlatform.Abstractions.Agent.Tools;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Tools.FieldTransform.Transforms;

/// <summary>
/// Sets a field value to <c>null</c>, preserving the key in the dictionary.
/// If the field is absent it is added with a <c>null</c> value.
/// </summary>
public sealed class ClearFieldTransform : IFieldTransform
{
    private readonly string _field;
    private readonly string _groupName;

    public string Type => "ClearField";
    public string Name { get; }

    public ClearFieldTransform(string name, string groupName, string field)
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
        var updated = new Dictionary<string, object?>(fields.Count);
        foreach (var kvp in fields)
            updated[kvp.Key] = kvp.Value;

        var oldValue = updated.TryGetValue(_field, out var ov) ? ov?.ToString() : null;
        updated[_field] = null;

        return new FieldTransformResult(
            updated,
            new List<FieldTransformAction>
            {
                new FieldTransformAction(_groupName, Name, Type, _field, true, oldValue, null)
            });
    }
}
