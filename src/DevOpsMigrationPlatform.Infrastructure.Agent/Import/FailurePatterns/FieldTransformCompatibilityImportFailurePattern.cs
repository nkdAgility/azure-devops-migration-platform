// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions.Agent.Import;
using DevOpsMigrationPlatform.Abstractions.Options;
using Microsoft.Extensions.Options;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Import.FailurePatterns;

internal sealed class FieldTransformCompatibilityImportFailurePattern : IImportFailurePattern
{
    public const string Code = "WORKITEMS_PREPARE_FIELD_TRANSFORM_COMPATIBILITY";
    private static readonly Regex ExpressionFieldTokenRegex = new(@"\b\w+\.\w+\b", RegexOptions.Compiled);

    private readonly FieldTransformOptions _fieldTransformOptions;

    public FieldTransformCompatibilityImportFailurePattern()
        : this(Options.Create(new FieldTransformOptions()))
    {
    }

    public FieldTransformCompatibilityImportFailurePattern(IOptions<FieldTransformOptions> fieldTransformOptions)
    {
        _fieldTransformOptions = fieldTransformOptions.Value;
    }

    public string PatternCode => Code;

    public async Task<IReadOnlyList<ImportFailureFinding>> EvaluateAsync(
        ImportFailurePatternContext context,
        CancellationToken cancellationToken)
    {
        if (!_fieldTransformOptions.Enabled || _fieldTransformOptions.TransformGroups.Count == 0)
        {
            return [];
        }

        var exportedFieldValues = new Dictionary<string, List<string?>>(System.StringComparer.OrdinalIgnoreCase);
        await foreach (var parsedRevision in WorkItemsPrepareRevisionReader.EnumerateAsync(
                           context.PrepareContext.ArtefactStore,
                           cancellationToken).ConfigureAwait(false))
        {
            if (parsedRevision.Revision is null)
            {
                continue;
            }

            foreach (var field in parsedRevision.Revision.Fields)
            {
                if (string.IsNullOrWhiteSpace(field.ReferenceName))
                {
                    continue;
                }

                if (!exportedFieldValues.TryGetValue(field.ReferenceName, out var values))
                {
                    values = [];
                    exportedFieldValues[field.ReferenceName] = values;
                }

                values.Add(field.Value);
            }
        }

        var findings = new List<ImportFailureFinding>();
        var groupIndex = 0;
        foreach (var group in _fieldTransformOptions.TransformGroups)
        {
            groupIndex++;
            if (!group.Enabled)
            {
                continue;
            }

            string groupName = string.IsNullOrWhiteSpace(group.Name) ? $"Group{groupIndex}" : group.Name!;
            var ruleIndex = 0;
            foreach (var rule in group.Transforms)
            {
                ruleIndex++;
                if (!rule.Enabled)
                {
                    continue;
                }

                string ruleName = string.IsNullOrWhiteSpace(rule.Name)
                    ? $"{groupName}.{rule.Type}{ruleIndex}"
                    : rule.Name!;
                var transformType = string.IsNullOrWhiteSpace(rule.Type) ? "<unspecified>" : rule.Type;
                foreach (var fieldReference in GetFieldReferences(rule))
                {
                    if (!exportedFieldValues.TryGetValue(fieldReference, out var values))
                    {
                        findings.Add(CreateFieldNotFoundFinding(ruleName, transformType, fieldReference));
                        continue;
                    }

                    if (RequiresNumericField(rule, fieldReference) && !ValuesAreNumeric(values))
                    {
                        findings.Add(CreateTypeMismatchFinding(ruleName, transformType, fieldReference));
                    }
                }
            }
        }

        return findings;
    }

    private static ImportFailureFinding CreateFieldNotFoundFinding(string ruleName, string transformType, string fieldReference)
        => new(
            Code,
            ImportFailureSeverity.Blocking,
            $"{FieldTransformFindingStatus.FieldNotFound}|{ruleName}|{fieldReference}|Unknown",
            $"FieldTransform rule '{ruleName}' ({transformType}) references '{fieldReference}', but that field is not present in exported revisions.",
            "Update FieldTransform configuration to use exported field reference names or re-export with the required fields.");

    private static ImportFailureFinding CreateTypeMismatchFinding(string ruleName, string transformType, string fieldReference)
        => new(
            Code,
            ImportFailureSeverity.Blocking,
            $"{FieldTransformFindingStatus.TypeMismatch}|{ruleName}|{fieldReference}|Numeric",
            $"FieldTransform rule '{ruleName}' ({transformType}) expects numeric values for '{fieldReference}', but non-numeric values were found in exported revisions.",
            "Adjust the transform expression or normalise source values so referenced fields are numeric before import.");

    private static bool RequiresNumericField(FieldTransformRuleOptions rule, string fieldReference)
    {
        if (!string.Equals(rule.Type, "CalculateField", System.StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(rule.Expression))
        {
            return false;
        }

        var expression = rule.Expression!;
        if (!ExpressionContainsMathOperators(expression))
        {
            return false;
        }

        return ExpressionFieldTokenRegex.Matches(expression)
            .Cast<Match>()
            .Any(m => string.Equals(m.Value, fieldReference, System.StringComparison.OrdinalIgnoreCase));
    }

    private static bool ExpressionContainsMathOperators(string expression)
        => expression.Contains('+')
           || expression.Contains('-')
           || expression.Contains('*')
           || expression.Contains('/')
           || expression.Contains('%');

    private static IReadOnlyList<string> GetFieldReferences(FieldTransformRuleOptions rule)
    {
        var references = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);

        AddIfPresent(rule.Field);
        AddIfPresent(rule.SourceField);
        AddIfPresent(rule.TargetField);
        if (rule.SourceFields is not null)
        {
            foreach (var sourceField in rule.SourceFields)
            {
                AddIfPresent(sourceField);
            }
        }

        if (!string.IsNullOrWhiteSpace(rule.Expression))
        {
            foreach (Match match in ExpressionFieldTokenRegex.Matches(rule.Expression!))
            {
                AddIfPresent(match.Value);
            }
        }

        return references.ToList();

        void AddIfPresent(string? fieldReference)
        {
            var normalized = fieldReference?.Trim();
            if (normalized is null || normalized.Length == 0)
                return;
            references.Add(normalized!);
        }
    }

    private static bool ValuesAreNumeric(IReadOnlyList<string?> values)
    {
        foreach (var value in values)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out _))
            {
                return false;
            }
        }

        return true;
    }
}

