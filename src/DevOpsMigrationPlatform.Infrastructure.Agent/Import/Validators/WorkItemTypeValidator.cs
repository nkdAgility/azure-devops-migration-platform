// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions.Agent.Import;
using DevOpsMigrationPlatform.Abstractions.Options;
using DevOpsMigrationPlatform.Infrastructure.Agent.Import.FailurePatterns;
using Microsoft.Extensions.Options;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Import.Validators;

internal sealed class WorkItemTypeValidator : IImportFailurePattern
{
    public const string Code = "WORKITEMS_PREPARE_MISSING_WORKITEM_TYPE";
    private const string WorkItemTypeReferenceName = "System.WorkItemType";
    private readonly IWorkItemTypeReadinessTargetFactory _typeReadinessTargetFactory;
    private readonly FieldTransformOptions _fieldTransformOptions;

    public WorkItemTypeValidator(IWorkItemTypeReadinessTargetFactory typeReadinessTargetFactory)
    {
        _typeReadinessTargetFactory = typeReadinessTargetFactory;
        _fieldTransformOptions = new FieldTransformOptions();
    }

    public WorkItemTypeValidator(
        IWorkItemTypeReadinessTargetFactory typeReadinessTargetFactory,
        IOptionsSnapshot<FieldTransformOptions> fieldTransformOptions)
    {
        _typeReadinessTargetFactory = typeReadinessTargetFactory;
        _fieldTransformOptions = fieldTransformOptions.Value;
    }

    public string PatternCode => Code;

    public async Task<IReadOnlyList<ImportFailureFinding>> EvaluateAsync(
        ImportFailurePatternContext context,
        CancellationToken cancellationToken)
    {
        var exportedTypes = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);

        await foreach (var parsedRevision in WorkItemsPrepareRevisionReader.EnumerateAsync(
                           context.PrepareContext.Package,
                           cancellationToken).ConfigureAwait(false))
        {
            if (parsedRevision.Revision is null)
            {
                continue;
            }

            foreach (var field in parsedRevision.Revision.Fields)
            {
                if (!string.Equals(field.ReferenceName, WorkItemTypeReferenceName, System.StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (field.Value is not string value || string.IsNullOrWhiteSpace(value))
                {
                    continue;
                }

                exportedTypes.Add(value.Trim());
            }
        }

        if (exportedTypes.Count == 0)
        {
            return [];
        }

        var mappedExportedTypes = BuildEffectiveTargetTypes(exportedTypes, ResolveFieldTransformOptions(context));
        var resolvedTypeExistence = new Dictionary<string, bool>(System.StringComparer.OrdinalIgnoreCase);
        var target = await _typeReadinessTargetFactory.CreateAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var findings = new List<ImportFailureFinding>();
            foreach (var workItemType in exportedTypes.OrderBy(t => t, System.StringComparer.OrdinalIgnoreCase))
            {
                var effectiveTargetType = mappedExportedTypes[workItemType];
                if (!resolvedTypeExistence.TryGetValue(effectiveTargetType, out var exists))
                {
                    exists = await target.WorkItemTypeExistsAsync(effectiveTargetType, cancellationToken).ConfigureAwait(false);
                    resolvedTypeExistence[effectiveTargetType] = exists;
                }

                if (exists)
                {
                    continue;
                }

                var mappingDescription = string.Equals(workItemType, effectiveTargetType, System.StringComparison.OrdinalIgnoreCase)
                    ? $"'{workItemType}'"
                    : $"'{workItemType}' (mapped to '{effectiveTargetType}')";
                findings.Add(new ImportFailureFinding(
                    PatternCode,
                    ImportFailureSeverity.Blocking,
                    workItemType,
                    $"Required work item type {mappingDescription} does not exist on the target project.",
                    $"Create or map '{workItemType}' on the target before running import."));
            }

            return findings;
        }
        finally
        {
            switch (target)
            {
                case IAsyncDisposable asyncDisposable:
                    await asyncDisposable.DisposeAsync().ConfigureAwait(false);
                    break;
                case System.IDisposable disposable:
                    disposable.Dispose();
                    break;
            }
        }
    }

    private Dictionary<string, string> BuildEffectiveTargetTypes(
        IReadOnlyCollection<string> exportedTypes,
        FieldTransformOptions fieldTransformOptions)
    {
        var resolvedTypes = exportedTypes.ToDictionary(
            type => type,
            type => type,
            System.StringComparer.OrdinalIgnoreCase);

        if (!fieldTransformOptions.Enabled || fieldTransformOptions.TransformGroups.Count == 0)
        {
            return resolvedTypes;
        }

        foreach (var group in fieldTransformOptions.TransformGroups)
        {
            if (!group.Enabled)
            {
                continue;
            }

            foreach (var rule in group.Transforms)
            {
                if (!rule.Enabled
                    || !string.Equals(rule.Type, "MapValue", System.StringComparison.OrdinalIgnoreCase)
                    || !string.Equals(rule.Field, WorkItemTypeReferenceName, System.StringComparison.OrdinalIgnoreCase)
                    || rule.ValueMap is null
                    || rule.ValueMap.Count == 0)
                {
                    continue;
                }

                foreach (var exportedType in exportedTypes)
                {
                    if (group.ApplyTo is { Count: > 0 } && !MatchesWorkItemType(group.ApplyTo, exportedType))
                    {
                        continue;
                    }

                    if (rule.ApplyTo is { Count: > 0 } && !MatchesWorkItemType(rule.ApplyTo, exportedType))
                    {
                        continue;
                    }

                    if (!rule.ValueMap.TryGetValue(resolvedTypes[exportedType], out var mappedType)
                        && !rule.ValueMap.TryGetValue(exportedType, out mappedType))
                    {
                        continue;
                    }

                    if (string.IsNullOrWhiteSpace(mappedType))
                    {
                        continue;
                    }

                    resolvedTypes[exportedType] = mappedType.Trim();
                }
            }
        }

        return resolvedTypes;
    }

    private FieldTransformOptions ResolveFieldTransformOptions(ImportFailurePatternContext context)
    {
        var configPayload = context.PrepareContext.Job.ConfigPayload ?? string.Empty;
        if (string.IsNullOrWhiteSpace(configPayload))
        {
            return _fieldTransformOptions;
        }

        try
        {
            using var doc = JsonDocument.Parse(configPayload);
            if (!doc.RootElement.TryGetProperty("MigrationPlatform", out var migrationPlatform)
                || migrationPlatform.ValueKind != JsonValueKind.Object
                || !migrationPlatform.TryGetProperty("Tools", out var tools)
                || tools.ValueKind != JsonValueKind.Object
                || !tools.TryGetProperty("FieldTransform", out var fieldTransform)
                || fieldTransform.ValueKind != JsonValueKind.Object)
            {
                return _fieldTransformOptions;
            }

            return JsonSerializer.Deserialize<FieldTransformOptions>(fieldTransform.GetRawText()) ?? _fieldTransformOptions;
        }
        catch
        {
            return _fieldTransformOptions;
        }
    }

    private static bool MatchesWorkItemType(IReadOnlyList<string> applyTo, string workItemType)
    {
        foreach (var candidate in applyTo)
        {
            if (string.Equals(candidate, workItemType, System.StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
