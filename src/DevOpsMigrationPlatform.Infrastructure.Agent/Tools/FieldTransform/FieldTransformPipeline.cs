using System;
using System.Collections.Generic;
using System.Diagnostics;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Agent.Tools;
using DevOpsMigrationPlatform.Abstractions.Options;
using Microsoft.Extensions.Logging;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Tools.FieldTransform;

/// <summary>
/// Executes all configured transform groups in order, applying per-group and per-rule
/// enabled/applyTo filters, and performing tag deduplication (FR-022) as a post-pass.
/// </summary>
internal sealed class FieldTransformPipeline
{
    private readonly IReadOnlyList<(FieldTransformGroupOptions Group, IReadOnlyList<(FieldTransformRuleOptions Rule, IFieldTransform Transform)> Transforms)> _groups;
    private readonly ILogger<FieldTransformPipeline> _logger;

    private static readonly ActivitySource s_activitySource = new(WellKnownActivitySourceNames.Migration);

    public FieldTransformPipeline(
        IReadOnlyList<(FieldTransformGroupOptions Group, IReadOnlyList<(FieldTransformRuleOptions Rule, IFieldTransform Transform)> Transforms)> groups,
        ILogger<FieldTransformPipeline> logger)
    {
        _groups = groups;
        _logger = logger;
    }

    public FieldTransformResult Execute(
        IReadOnlyDictionary<string, object?> fields,
        FieldTransformContext context)
    {
        using var pipelineActivity = s_activitySource.StartActivity("fieldtransform.pipeline.execute");
        pipelineActivity?.SetTag("wi.id", context.WorkItemId);
        pipelineActivity?.SetTag("group_count", _groups.Count);

        var current = CopyDictionary(fields);
        var allActions = new List<FieldTransformAction>();
        bool tagTransformFired = false;

        foreach (var (group, transforms) in _groups)
        {
            if (!group.Enabled) continue;

            if (group.ApplyTo != null && group.ApplyTo.Count > 0 && !MatchesWorkItemType(group.ApplyTo, context.WorkItemType))
                continue;

            var groupName = group.Name ?? "unnamed";
            _logger.LogDebug(
                "Executing transform group '{GroupName}' ({TransformCount} transforms)",
                groupName, transforms.Count);

            using var groupActivity = s_activitySource.StartActivity("fieldtransform.group.execute");
            groupActivity?.SetTag("group.name", groupName);
            groupActivity?.SetTag("wi.id", context.WorkItemId);

            foreach (var (rule, transform) in transforms)
            {
                if (!rule.Enabled) continue;

                if (rule.ApplyTo != null && rule.ApplyTo.Count > 0 && !MatchesWorkItemType(rule.ApplyTo, context.WorkItemType))
                    continue;

                var result = transform.Apply(current, context);
                current = CopyDictionary(result.Fields);
                allActions.AddRange(result.Actions);

                foreach (var action in result.Actions)
                {
                    if (action.Modified && string.Equals(action.Field, "System.Tags", StringComparison.OrdinalIgnoreCase))
                        tagTransformFired = true;
                }
            }
        }

        // Tag deduplication post-pass (FR-022)
        if (tagTransformFired && current.TryGetValue("System.Tags", out var rawTags))
        {
            var deduped = TagDeduplicator.Deduplicate(rawTags?.ToString() ?? string.Empty);
            current["System.Tags"] = deduped;
        }

        int modifiedCount = 0;
        foreach (var action in allActions)
            if (action.Modified) modifiedCount++;

        _logger.LogDebug(
            "Field transform pipeline complete: {ActionCount} actions, {ModifiedCount} modifications",
            allActions.Count, modifiedCount);

        return new FieldTransformResult(current, allActions);
    }

    private static bool MatchesWorkItemType(IReadOnlyList<string> applyTo, string workItemType)
    {
        foreach (var t in applyTo)
        {
            if (string.Equals(t, workItemType, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private static Dictionary<string, object?> CopyDictionary(IReadOnlyDictionary<string, object?> source)
    {
        var copy = new Dictionary<string, object?>(source.Count);
        foreach (var kvp in source)
            copy[kvp.Key] = kvp.Value;
        return copy;
    }
}

/// <summary>Deduplicates semicolon-separated tags (case-insensitive, first-occurrence wins).</summary>
internal static class TagDeduplicator
{
    public static string Deduplicate(string tags)
    {
        if (string.IsNullOrWhiteSpace(tags)) return tags;

        var parts = tags.Split(new char[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<string>();

        foreach (var part in parts)
        {
            var trimmed = part.Trim();
            if (!string.IsNullOrEmpty(trimmed) && seen.Add(trimmed))
                result.Add(trimmed);
        }

        return string.Join("; ", result);
    }
}
