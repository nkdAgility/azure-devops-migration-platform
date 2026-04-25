using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using DevOpsMigrationPlatform.Abstractions.Agent.Tools;
using Microsoft.Extensions.Logging;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Tools.FieldTransform.Transforms;

/// <summary>
/// Evaluates a regex <c>_condition</c> against <c>_conditionField</c> and writes
/// <c>_trueValue</c> or <c>_falseValue</c> to <c>_targetField</c> accordingly.
/// Uses a 1-second timeout to guard against ReDoS (FR-018).
/// </summary>
public sealed class ConditionalFieldTransform : IFieldTransform
{
    private readonly Regex _condition;
    private readonly string _conditionField;
    private readonly string _targetField;
    private readonly string? _trueValue;
    private readonly string? _falseValue;
    private readonly string _groupName;
    private readonly ILogger<ConditionalFieldTransform> _logger;

    public string Type => "ConditionalField";
    public string Name { get; }

    public ConditionalFieldTransform(
        string name,
        string groupName,
        string conditionField,
        string condition,
        string targetField,
        string? trueValue,
        string? falseValue,
        ILogger<ConditionalFieldTransform> logger)
    {
        Name = name;
        _groupName = groupName;
        _conditionField = conditionField;
        _targetField = targetField;
        _trueValue = trueValue;
        _falseValue = falseValue;
        _logger = logger;

        try
        {
            _condition = new Regex(condition, RegexOptions.None, TimeSpan.FromSeconds(1));
        }
        catch (ArgumentException ex)
        {
            throw new ArgumentException(
                $"ConditionalField transform '{name}': invalid condition pattern '{condition}'. {ex.Message}", ex);
        }
    }

    /// <inheritdoc />
    public FieldTransformResult Apply(
        IReadOnlyDictionary<string, object?> fields,
        FieldTransformContext context)
    {
        bool matches;
        try
        {
            var raw = fields.TryGetValue(_conditionField, out var v) ? v?.ToString() ?? string.Empty : string.Empty;
            matches = _condition.IsMatch(raw);
        }
        catch (RegexMatchTimeoutException ex)
        {
            _logger.LogError(ex,
                "ConditionalField transform '{Name}': condition pattern '{Pattern}' timed out on field '{Field}'. Aborting revision.",
                Name, _condition.ToString(), _conditionField);
            throw new InvalidOperationException(
                $"ConditionalField transform '{Name}': condition pattern '{_condition}' timed out on field '{_conditionField}'.", ex);
        }

        var assignValue = matches ? _trueValue : _falseValue;

        var updated = new Dictionary<string, object?>(fields.Count + 1);
        foreach (var kvp in fields)
            updated[kvp.Key] = kvp.Value;

        var oldValue = updated.TryGetValue(_targetField, out var ov) ? ov?.ToString() : null;
        updated[_targetField] = assignValue;

        return new FieldTransformResult(updated,
            new List<FieldTransformAction>
            {
                new FieldTransformAction(_groupName, Name, Type, _targetField, true, oldValue, assignValue)
            });
    }
}
