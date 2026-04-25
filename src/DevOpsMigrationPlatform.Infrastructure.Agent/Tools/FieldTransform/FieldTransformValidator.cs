using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
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
        var entries = new List<FieldTransformValidationEntry>();

        if (!_options.Enabled)
            return new FieldTransformValidationReport(true, entries);

        ValidateStructure(entries);

        if (_providerFactory != null)
        {
            await ValidateFieldReferencesAsync(entries, cancellationToken).ConfigureAwait(false);
        }

        bool isValid = !entries.Exists(e => e.Severity == FieldTransformValidationSeverity.Error);
        return new FieldTransformValidationReport(isValid, entries);
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
