using System.Collections.Generic;

namespace DevOpsMigrationPlatform.Abstractions.Agent.Tools;

/// <summary>
/// Evaluates a string expression against a set of work item field values.
/// Used by conditional and computed-field transforms.
/// </summary>
public interface IExpressionEvaluator
{
    /// <summary>
    /// Evaluates <paramref name="expression"/> using <paramref name="fieldValues"/> as the variable scope
    /// and returns the result, or <c>null</c> if the expression yields no value.
    /// </summary>
    object? Evaluate(string expression, IReadOnlyDictionary<string, object?> fieldValues);
}
