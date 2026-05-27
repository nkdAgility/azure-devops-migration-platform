// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Agent.Tools;
using DevOpsMigrationPlatform.Abstractions.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Tools.FieldTransform;

/// <summary>
/// Validates all configured transform rules against the source field definitions.
/// If <paramref name="providerFactory"/> is <c>null</c>, provider-based field validation is skipped.
/// </summary>
public sealed class FieldTransformValidator : IFieldTransformValidator
{
    private readonly FieldTransformOptions _options;
    private readonly IFieldDefinitionProviderFactory? _providerFactory;
    private readonly ILogger<FieldTransformValidator> _logger;

    private static readonly ActivitySource s_activitySource = new(WellKnownActivitySourceNames.Migration);

    public FieldTransformValidator(
        IOptions<FieldTransformOptions> options,
        ILogger<FieldTransformValidator> logger,
        IFieldDefinitionProviderFactory? providerFactory = null)
    {
        _options = options.Value;
        _logger = logger;
        _providerFactory = providerFactory;
    }

    /// <inheritdoc />
    public async Task<FieldTransformValidationReport> ValidateAsync(
        int sampleSize = 10,
        CancellationToken cancellationToken = default)
    {
        int transformCount = 0;
        foreach (var g in _options.TransformGroups) transformCount += g.Transforms.Count;

        _logger.LogInformation(
            "FieldTransform validation starting ({TransformCount} transforms, {GroupCount} groups)",
            transformCount, _options.TransformGroups.Count);

        using var activity = s_activitySource.StartActivity("fieldtransform.validate");
        activity?.SetTag("module", "FieldTransform");
        activity?.SetTag("transform_count", transformCount);

        var sw = Stopwatch.StartNew();
        try
        {
            var entries = new List<FieldTransformValidationEntry>();

            if (!_options.Enabled)
            {
                sw.Stop();
                _logger.LogInformation(
                    "FieldTransform validation skipped: tool is disabled ({DurationMs}ms)", sw.ElapsedMilliseconds);
                return new FieldTransformValidationReport(true, entries);
            }

            ValidateStructure(entries);

            if (_providerFactory != null)
            {
                await ValidateFieldReferencesAsync(entries, cancellationToken).ConfigureAwait(false);
            }

            bool isValid = !entries.Exists(e => e.Severity == FieldTransformValidationSeverity.Error);
            int errorCount = entries.FindAll(e => e.Severity == FieldTransformValidationSeverity.Error).Count;
            int warnCount = entries.FindAll(e => e.Severity == FieldTransformValidationSeverity.Warning).Count;

            sw.Stop();
            activity?.SetTag("is_valid", isValid);
            activity?.SetTag("error_count", errorCount);

            _logger.LogInformation(
                "FieldTransform validation complete: {IsValid}, {ErrorCount} errors, {WarnCount} warnings in {DurationMs}ms",
                isValid, errorCount, warnCount, sw.ElapsedMilliseconds);

            return new FieldTransformValidationReport(isValid, entries);
        }
        catch (Exception ex)
        {
            sw.Stop();
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            _logger.LogError(
                ex,
                "FieldTransform validation failed after {DurationMs}ms: {ErrorType}",
                sw.ElapsedMilliseconds, ex.GetType().Name);
            throw;
        }
    }

    private void ValidateStructure(List<FieldTransformValidationEntry> entries)
    {
        int gi = 0;
        foreach (var group in _options.TransformGroups)
        {
            gi++;
            var groupName = group.Name ?? $"Group{gi}";

            int ti = 0;
            foreach (var rule in group.Transforms)
            {
                ti++;
                var transformName = rule.Name ?? $"{groupName}.{rule.Type}{ti}";

                if (string.IsNullOrWhiteSpace(rule.Type))
                {
                    entries.Add(new FieldTransformValidationEntry(
                        groupName, transformName, string.Empty, FieldTransformValidationSeverity.Error,
                        "Transform type must not be empty."));
                    _logger.LogError(
                        "Transform {GroupName}.{TransformName} has an empty type.",
                        groupName, transformName);
                }
            }
        }
    }

    private async Task ValidateFieldReferencesAsync(
        List<FieldTransformValidationEntry> entries,
        CancellationToken cancellationToken)
    {
        var sourceProvider = _providerFactory!.Create("source");
        var sourceDefs = await sourceProvider
            .GetFieldDefinitionsAsync(cancellationToken)
            .ConfigureAwait(false);

        var sourceDefMap = new Dictionary<string, FieldDefinition>(StringComparer.OrdinalIgnoreCase);
        foreach (var def in sourceDefs)
            sourceDefMap[def.ReferenceName] = def;

        int gi = 0;
        foreach (var group in _options.TransformGroups)
        {
            gi++;
            var groupName = group.Name ?? $"Group{gi}";
            if (!group.Enabled) continue;

            int ti = 0;
            foreach (var rule in group.Transforms)
            {
                ti++;
                var transformName = rule.Name ?? $"{groupName}.{rule.Type}{ti}";
                if (!rule.Enabled) continue;

                var fieldsToCheck = GetFieldReferences(rule);
                foreach (var fieldRef in fieldsToCheck)
                {
                    if (!sourceDefMap.ContainsKey(fieldRef))
                    {
                        entries.Add(new FieldTransformValidationEntry(
                            groupName, transformName, fieldRef,
                            FieldTransformValidationSeverity.Error,
                            $"Field '{fieldRef}' does not exist in the source system."));
                        _logger.LogError(
                            "Field {Field} referenced by {GroupName}.{TransformName} does not exist in the source.",
                            fieldRef, groupName, transformName);
                    }
                }

                ValidateCopyTypeCompatibility(entries, sourceDefMap, groupName, transformName, rule);
                ValidateMapValueTargets(entries, sourceDefMap, groupName, transformName, rule);
            }
        }
    }

    private static void ValidateCopyTypeCompatibility(
        List<FieldTransformValidationEntry> entries,
        IReadOnlyDictionary<string, FieldDefinition> sourceDefMap,
        string groupName,
        string transformName,
        FieldTransformRuleOptions rule)
    {
        if (!string.Equals(rule.Type, "Copy", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(rule.SourceField) || string.IsNullOrWhiteSpace(rule.TargetField))
        {
            return;
        }

        var sourceField = rule.SourceField!;
        var targetField = rule.TargetField!;

        if (!sourceDefMap.TryGetValue(sourceField, out var sourceDef) ||
            !sourceDefMap.TryGetValue(targetField, out var targetDef))
        {
            return;
        }

        if (!string.Equals(sourceDef.Type, targetDef.Type, StringComparison.OrdinalIgnoreCase))
        {
            entries.Add(new FieldTransformValidationEntry(
                groupName,
                transformName,
                targetField,
                FieldTransformValidationSeverity.Warning,
                $"Potential type incompatibility: '{sourceField}' ({sourceDef.Type}) to '{targetField}' ({targetDef.Type})."));
        }
    }

    private static void ValidateMapValueTargets(
        List<FieldTransformValidationEntry> entries,
        IReadOnlyDictionary<string, FieldDefinition> sourceDefMap,
        string groupName,
        string transformName,
        FieldTransformRuleOptions rule)
    {
        if (!string.Equals(rule.Type, "MapValue", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(rule.Field) || rule.ValueMap == null || rule.ValueMap.Count == 0)
        {
            return;
        }

        var fieldName = rule.Field!;

        if (!sourceDefMap.TryGetValue(fieldName, out var sourceDef) ||
            sourceDef.AllowedValues == null ||
            sourceDef.AllowedValues.Count == 0)
        {
            return;
        }

        foreach (var mappedValue in rule.ValueMap.Values)
        {
            if (!sourceDef.AllowedValues.Any(allowed =>
                string.Equals(allowed, mappedValue, StringComparison.OrdinalIgnoreCase)))
            {
                entries.Add(new FieldTransformValidationEntry(
                    groupName,
                    transformName,
                    fieldName,
                    FieldTransformValidationSeverity.Warning,
                    $"Mapped value '{mappedValue}' is not in allowed values for '{fieldName}'."));
            }
        }
    }

    private static List<string> GetFieldReferences(FieldTransformRuleOptions rule)
    {
        var refs = new List<string>();
        if (!string.IsNullOrWhiteSpace(rule.Field)) refs.Add(rule.Field!);
        if (!string.IsNullOrWhiteSpace(rule.SourceField)) refs.Add(rule.SourceField!);
        if (!string.IsNullOrWhiteSpace(rule.TargetField)) refs.Add(rule.TargetField!);
        if (rule.SourceFields != null)
            foreach (var f in rule.SourceFields)
                if (!string.IsNullOrWhiteSpace(f)) refs.Add(f);
        return refs;
    }
}
