// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Telemetry;
using DevOpsMigrationPlatform.Abstractions.Agent.Context;
using DevOpsMigrationPlatform.Abstractions.Agent.Telemetry;
using DevOpsMigrationPlatform.Abstractions.Agent.Tools;
using DevOpsMigrationPlatform.Abstractions.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Tools.FieldTransform;

/// <summary>
/// Entry-point for field transformation. Builds the <see cref="FieldTransformPipeline"/>
/// from configuration and delegates execution to it.
/// <para>
/// Registered as a DI <b>singleton</b> per the Tool contract (ADR-0026, TC-M2). Per-job
/// configuration is honoured via config-accessor indirection: options are re-resolved
/// through <see cref="IOptionsFactory{TOptions}"/> whenever the
/// <see cref="ICurrentPackageConfigAccessor"/> current configuration changes, so each
/// job sees the options from its own <c>migration-config.json</c> while the tool itself
/// holds no per-job state.
/// </para>
/// </summary>
public sealed class FieldTransformTool : IFieldTransformTool
{
    private readonly ILogger<FieldTransformTool> _logger;
    private readonly ILoggerFactory _loggerFactory;
    private readonly IFieldTransformFactory _factory;
    private readonly IPlatformMetrics? _metrics;

    // Config-accessor indirection (DI/singleton path). Null when the fixed-options
    // constructor is used (tests / direct composition).
    private readonly ICurrentPackageConfigAccessor? _configAccessor;
    private readonly IOptionsFactory<FieldTransformOptions>? _optionsFactory;

    private readonly object _sync = new();
    private (object? ConfigKey, FieldTransformOptions Options, FieldTransformPipeline Pipeline)? _cached;

    private static readonly ActivitySource s_activitySource =
        new(WellKnownActivitySourceNames.Migration);

    /// <summary>
    /// Singleton DI constructor (ADR-0026, TC-M2): options are resolved per call from the
    /// current package configuration so one instance serves every job scope.
    /// </summary>
    public FieldTransformTool(
        ICurrentPackageConfigAccessor configAccessor,
        IOptionsFactory<FieldTransformOptions> optionsFactory,
        IFieldTransformFactory factory,
        ILoggerFactory loggerFactory,
        IPlatformMetrics? metrics = null)
    {
        _configAccessor = configAccessor ?? throw new System.ArgumentNullException(nameof(configAccessor));
        _optionsFactory = optionsFactory ?? throw new System.ArgumentNullException(nameof(optionsFactory));
        _factory = factory ?? throw new System.ArgumentNullException(nameof(factory));
        _loggerFactory = loggerFactory ?? throw new System.ArgumentNullException(nameof(loggerFactory));
        _logger = loggerFactory.CreateLogger<FieldTransformTool>();
        _metrics = metrics;
    }

    /// <summary>
    /// Fixed-options constructor: builds the pipeline once from the supplied options.
    /// </summary>
    public FieldTransformTool(
        IOptions<FieldTransformOptions> options,
        IFieldTransformFactory factory,
        ILoggerFactory loggerFactory,
        IPlatformMetrics? metrics = null)
    {
        _factory = factory ?? throw new System.ArgumentNullException(nameof(factory));
        _loggerFactory = loggerFactory ?? throw new System.ArgumentNullException(nameof(loggerFactory));
        _logger = loggerFactory.CreateLogger<FieldTransformTool>();
        _metrics = metrics;

        var opts = options.Value;
        _cached = (null, opts, BuildPipeline(opts));
    }

    private (FieldTransformOptions Options, FieldTransformPipeline Pipeline) Resolve()
    {
        if (_optionsFactory is null)
        {
            var fixedCache = _cached!.Value;
            return (fixedCache.Options, fixedCache.Pipeline);
        }

        var key = (object?)_configAccessor!.Current;
        lock (_sync)
        {
            if (_cached is { } cached && ReferenceEquals(cached.ConfigKey, key))
                return (cached.Options, cached.Pipeline);

            // IOptionsFactory runs the Configure delegates (binding from the current
            // package config) and all IValidateOptions validators on every Create.
            var opts = _optionsFactory.Create(Microsoft.Extensions.Options.Options.DefaultName);
            var pipeline = BuildPipeline(opts);
            _cached = (key, opts, pipeline);
            return (opts, pipeline);
        }
    }

    private FieldTransformPipeline BuildPipeline(FieldTransformOptions options)
    {
        // Warn if total transforms exceed recommended limit (FR-023)
        int totalTransforms = 0;
        foreach (var group in options.TransformGroups)
            totalTransforms += group.Transforms.Count;

        if (totalTransforms > 100)
            _logger.LogWarning(
                "FieldTransformTool: {Count} transforms configured. Performance may be affected (FR-023).",
                totalTransforms);

        var groups = new List<(FieldTransformGroupOptions, IReadOnlyList<(FieldTransformRuleOptions, IFieldTransform)>)>();
        int gi = 0;

        foreach (var group in options.TransformGroups)
        {
            gi++;
            var groupName = group.Name ?? $"Group{gi}";
            var transforms = new List<(FieldTransformRuleOptions, IFieldTransform)>();

            for (int i = 0; i < group.Transforms.Count; i++)
            {
                var rule = group.Transforms[i];
                var transform = _factory.Create(rule, groupName, i + 1);
                transforms.Add((rule, transform));
            }

            groups.Add((group, transforms));
        }

        return new FieldTransformPipeline(groups, _loggerFactory.CreateLogger<FieldTransformPipeline>());
    }

    /// <inheritdoc />
    public FieldTransformResult ApplyTransforms(IReadOnlyDictionary<string, object?> fields, FieldTransformContext context)
    {
        var (_, pipeline) = Resolve();

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
            var result = pipeline.Execute(fields, context);
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
        var (options, _) = Resolve();
        if (!options.Enabled) return false;

        foreach (var group in options.TransformGroups)
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
