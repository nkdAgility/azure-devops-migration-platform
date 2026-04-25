using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using DevOpsMigrationPlatform.Abstractions.Agent.Tools;
using Microsoft.Extensions.Logging;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Tools.FieldTransform.Transforms;

/// <summary>
/// Applies a compiled regex find-and-replace to a single field value (FR-018).
/// The pattern is validated and compiled at construction time.
/// A 1-second timeout guards against ReDoS.
/// </summary>
public sealed class RegexFieldTransform : IFieldTransform
{
    private readonly Regex _regex;
    private readonly string _field;
    private readonly string _replacement;
    private readonly string _groupName;
    private readonly ILogger<RegexFieldTransform> _logger;

    public string Type => "RegexField";
    public string Name { get; }

    public RegexFieldTransform(
        string name,
        string groupName,
        string field,
        string pattern,
        string replacement,
        ILogger<RegexFieldTransform> logger)
    {
        Name = name;
        _groupName = groupName;
        _field = field;
        _replacement = replacement;
        _logger = logger;

        try
        {
            _regex = new Regex(pattern, RegexOptions.None, TimeSpan.FromSeconds(1));
        }
        catch (ArgumentException ex)
        {
            throw new ArgumentException(
                $"RegexField transform '{name}': invalid pattern '{pattern}'. {ex.Message}", ex);
        }
    }

    /// <inheritdoc />
    public FieldTransformResult Apply(
        IReadOnlyDictionary<string, object?> fields,
        FieldTransformContext context)
    {
        if (!fields.TryGetValue(_field, out var rawValue))
            return new FieldTransformResult(fields, new List<FieldTransformAction>());

        var oldValue = rawValue?.ToString() ?? string.Empty;

        try
        {
            var newValue = _regex.Replace(oldValue, _replacement);

            if (string.Equals(oldValue, newValue, StringComparison.Ordinal))
            {
                return new FieldTransformResult(fields,
                    new List<FieldTransformAction>
                    {
                        new FieldTransformAction(_groupName, Name, Type, _field, false, oldValue, newValue)
                    });
            }

            var updated = new Dictionary<string, object?>(fields.Count);
            foreach (var kvp in fields)
                updated[kvp.Key] = kvp.Value;
            updated[_field] = newValue;

            return new FieldTransformResult(updated,
                new List<FieldTransformAction>
                {
                    new FieldTransformAction(_groupName, Name, Type, _field, true, oldValue, newValue)
                });
        }
        catch (RegexMatchTimeoutException ex)
        {
            _logger.LogError(ex,
                "RegexField transform '{Name}': pattern '{Pattern}' timed out on field '{Field}'. Aborting revision.",
                Name, _regex.ToString(), _field);
            throw new InvalidOperationException(
                $"RegexField transform '{Name}': pattern '{_regex}' timed out on field '{_field}'.", ex);
        }
    }
}
