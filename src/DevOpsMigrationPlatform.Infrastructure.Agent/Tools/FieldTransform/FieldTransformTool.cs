// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Telemetry;
using DevOpsMigrationPlatform.Abstractions.Agent.Telemetry;
using DevOpsMigrationPlatform.Abstractions.Agent.Tools;
using DevOpsMigrationPlatform.Abstractions.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Tools.FieldTransform;

/// <summary>
/// Entry-point for field transformation. Builds the <see cref="FieldTransformPipeline"/>
/// from configuration and delegates execution to it.
/// </summary>
public sealed class FieldTransformTool : IFieldTransformTool
{
    private readonly FieldTransformOptions _options;
    private readonly FieldTransformPipeline _pipeline;
    private readonly ILogger<FieldTransformTool> _logger;
    private readonly IPlatformMetrics? _metrics;

    private static readonly ActivitySource s_activitySource =
        new(WellKnownActivitySourceNames.Migration);

    public FieldTransformTool(
        IOptions<FieldTransformOptions> options,
        IFieldTransformFactory factory,
        ILoggerFactory loggerFactory,
        IPlatformMetrics? metrics = null)
    {
        _options = options.Value;
        _logger = loggerFactory.CreateLogger<FieldTransformTool>();
        _metrics = metrics;

        // Warn if total transforms exceed recommended limit (FR-023)
        int totalTransforms = 0;
        foreach (var group in _options.TransformGroups)
            totalTransforms += group.Transforms.Count;

        if (totalTransforms > 100)
            _logger.LogWarning(
                "FieldTransformTool: {Count} transforms configured. Performance may be affected (FR-023).",
                totalTransforms);

        var groups = new List<(FieldTransformGroupOptions, IReadOnlyList<(FieldTransformRuleOptions, IFieldTransform)>)>();
        int gi = 0;

        foreach (var group in _options.TransformGroups)
        {
            gi++;
            var groupName = group.Name ?? $"Group{gi}";
            var transforms = new List<(FieldTransformRuleOptions, IFieldTransform)>();

            for (int i = 0; i < group.Transforms.Count; i++)
            {
                var rule = group.Transforms[i];
                var transform = factory.Create(rule, groupName, i + 1);
                transforms.Add((rule, transform));
            }

            groups.Add((group, transforms));
        }

        _pipeline = new FieldTransformPipeline(groups, loggerFactory.CreateLogger<FieldTransformPipeline>());
    }

    /// <inheritdoc />
    public FieldTransformResult ApplyTransforms(IReadOnlyDictionary<string, object?> fields, FieldTransformContext context)
    {
        var tags = new MetricsTagList
        {
            { "operation", PhaseToOperationTag(context.Phase) },
            { "module", "FieldTransform" },
        };
        // Inherit job.id from parent activity if available
        var jobId = Activity.Current?.GetTagItem("job.id") as string;
        if (jobId != null) tags.Add("job.id", jobId);

        _logger.LogInformation(
            "FieldTransform starting for WI {WorkItemId} revision {RevisionIndex} phase {Phase}",
            context.WorkItemId, context.RevisionIndex, context.Phase);

        using var activity = s_activitySource.StartActivity("fieldtransform.apply");
        activity?.SetTag("wi.id", context.WorkItemId);
        activity?.SetTag("wi.type", context.WorkItemType);
        activity?.SetTag("revision.index", context.RevisionIndex);
        activity?.SetTag("operation", PhaseToOperationTag(context.Phase));
        activity?.SetTag("module", "FieldTransform");
        if (jobId != null) activity?.SetTag("job.id", jobId);

        _metrics?.IncrementFieldTransformInFlight(tags);
        var sw = Stopwatch.StartNew();
        try
        {
            var result = _pipeline.Execute(fields, context);
            sw.Stop();

            int modifiedCount = 0;
            foreach (var action in result.Actions)
                if (action.Modified) modifiedCount++;

            _metrics?.RecordFieldTransformApplied(tags);
            _metrics?.RecordFieldTransformDuration(sw.Elapsed.TotalMilliseconds, tags);
            _metrics?.RecordFieldTransformFieldsModified(modifiedCount, tags);

            _logger.LogInformation(
                "FieldTransform complete for WI {WorkItemId} revision {RevisionIndex}: {ActionsCount} actions, {FieldsModified} fields modified in {DurationMs}ms",
                context.WorkItemId, context.RevisionIndex, result.Actions.Count, modifiedCount, (long)sw.Elapsed.TotalMilliseconds);

            return result;
        }
        catch (System.Exception ex)
        {
            sw.Stop();
            _metrics?.RecordFieldTransformError(tags);
            _logger.LogError(
                ex,
                "FieldTransform failed for WI {WorkItemId} revision {RevisionIndex} phase {Phase}: {ErrorType}",
                context.WorkItemId, context.RevisionIndex, context.Phase, ex.GetType().Name);
            throw;
        }
        finally
        {
            _metrics?.DecrementFieldTransformInFlight(tags);
        }
    }

    /// <inheritdoc />
    public bool IsEnabledForPhase(FieldTransformPhase phase)
    {
        if (!_options.Enabled) return false;

        foreach (var group in _options.TransformGroups)
        {
            if (!group.Enabled) continue;
            foreach (var rule in group.Transforms)
            {
                if (rule.Enabled) return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Maps a <see cref="FieldTransformPhase"/> to the canonical <c>operation</c> tag value
    /// used on metrics and spans. Values: <c>import</c>, <c>export</c>, <c>update</c>.
    /// <c>Both</c> is a configuration sentinel — execution always runs in a concrete phase.
    /// </summary>
    private static string PhaseToOperationTag(FieldTransformPhase phase) => phase switch
    {
        FieldTransformPhase.Export => "export",
        FieldTransformPhase.Import => "import",
        FieldTransformPhase.Update => "update",
        _ => "import"  // Both is a config sentinel; concrete execution defaults to import
    };
}


