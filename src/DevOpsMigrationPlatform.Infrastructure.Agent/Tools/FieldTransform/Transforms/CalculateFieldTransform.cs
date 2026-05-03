// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Collections.Generic;
using DevOpsMigrationPlatform.Abstractions.Agent.Tools;
using Microsoft.Extensions.Logging;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Tools.FieldTransform.Transforms;

/// <summary>
/// Evaluates an expression against the current field dictionary and stores the
/// result in <c>_field</c>.  Expression evaluation failures are caught and logged;
/// the original fields are returned unchanged (FR-016).
/// </summary>
public sealed class CalculateFieldTransform : IFieldTransform
{
    private readonly string _field;
    private readonly string _expression;
    private readonly IExpressionEvaluator _evaluator;
    private readonly string _groupName;
    private readonly ILogger<CalculateFieldTransform> _logger;

    public string Type => "CalculateField";
    public string Name { get; }

    public CalculateFieldTransform(
        string name,
        string groupName,
        string field,
        string expression,
        IExpressionEvaluator evaluator,
        ILogger<CalculateFieldTransform> logger)
    {
        Name = name;
        _groupName = groupName;
        _field = field;
        _expression = expression;
        _evaluator = evaluator;
        _logger = logger;
    }

    /// <inheritdoc />
    public FieldTransformResult Apply(
        IReadOnlyDictionary<string, object?> fields,
        FieldTransformContext context)
    {
        try
        {
            var result = _evaluator.Evaluate(_expression, fields);
            var updated = new Dictionary<string, object?>(fields.Count);
            foreach (var kvp in fields)
                updated[kvp.Key] = kvp.Value;

            var oldValue = updated.TryGetValue(_field, out var ov) ? ov?.ToString() : null;
            updated[_field] = result;

            return new FieldTransformResult(
                updated,
                new List<FieldTransformAction>
                {
                    new FieldTransformAction(_groupName, Name, Type, _field, true, oldValue, result?.ToString())
                });
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "CalculateFieldTransform '{Name}': expression '{Expression}' failed.",
                Name,
                _expression);

            var action = new FieldTransformAction(_groupName, Name, Type, _field, false, null, null);
            return new FieldTransformResult(fields, new List<FieldTransformAction> { action });
        }
    }
}
