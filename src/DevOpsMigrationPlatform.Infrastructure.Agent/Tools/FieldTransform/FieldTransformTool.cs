using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
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

    private static readonly ActivitySource ActivitySource =
        new ActivitySource("DevOpsMigrationPlatform.FieldTransformTool");

    public FieldTransformTool(
        IOptions<FieldTransformOptions> options,
        IFieldTransformFactory factory,
        ILoggerFactory loggerFactory)
    {
        _options = options.Value;
        _logger = loggerFactory.CreateLogger<FieldTransformTool>();

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
        _logger.LogDebug(
            "Applying field transforms to work item {WorkItemId} revision {RevisionIndex} phase {Phase}",
            context.WorkItemId, context.RevisionIndex, context.Phase);

        using var activity = ActivitySource.StartActivity("FieldTransformTool.ApplyTransforms");
        activity?.SetTag("work_item_id", context.WorkItemId);
        activity?.SetTag("revision_index", context.RevisionIndex);
        activity?.SetTag("phase", context.Phase.ToString());

        return _pipeline.Execute(fields, context);
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
}
