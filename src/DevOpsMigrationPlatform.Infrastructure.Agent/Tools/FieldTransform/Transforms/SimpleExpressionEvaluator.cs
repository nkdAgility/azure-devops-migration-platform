using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using DevOpsMigrationPlatform.Abstractions.Agent.Tools;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Tools.FieldTransform.Transforms;

/// <summary>
/// Evaluates a simple arithmetic expression with field-reference substitution.
/// Supports the four arithmetic operators (+, -, *, /) and the modulo operator (%).
/// Field references are resolved by name from the current field dictionary.
/// A missing field reference causes the transform to fail gracefully (FR-016).
/// </summary>
public sealed class SimpleExpressionEvaluator : IExpressionEvaluator
{
    /// <inheritdoc />
    public object? Evaluate(string expression, IReadOnlyDictionary<string, object?> fieldValues)
    {
        string resolved = expression;
        foreach (var kvp in fieldValues)
            resolved = resolved.Replace(kvp.Key, kvp.Value?.ToString() ?? string.Empty);

        // Detect unresolved field references that look like "Word.Word"
        var unresolved = Regex.Match(resolved, @"\b\w+\.\w+\b");
        if (unresolved.Success)
            throw new InvalidOperationException(
                $"Field reference '{unresolved.Value}' could not be resolved.");

        if (TryEvaluateArithmetic(resolved.Trim(), out var numericResult))
        {
            if (numericResult == Math.Floor(numericResult))
                return ((long)numericResult).ToString(CultureInfo.InvariantCulture);
            return numericResult.ToString(CultureInfo.InvariantCulture);
        }

        return resolved;
    }

    private static bool TryEvaluateArithmetic(string expression, out double result)
    {
        result = 0;
        try
        {
            var dt = new System.Data.DataTable();
            var computed = dt.Compute(expression, string.Empty);
            result = Convert.ToDouble(computed, CultureInfo.InvariantCulture);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
