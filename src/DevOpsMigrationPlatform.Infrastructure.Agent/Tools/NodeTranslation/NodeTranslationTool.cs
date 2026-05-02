#if !NET481
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text.RegularExpressions;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Agent.Telemetry;
using DevOpsMigrationPlatform.Abstractions.Agent.Tools;
using DevOpsMigrationPlatform.Abstractions.Options;
using DevOpsMigrationPlatform.Abstractions.Telemetry;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Tools.NodeTranslation;

/// <summary>
/// Pure path-mapping tool. Pre-compiles all <see cref="NodeMapping"/> patterns at construction time.
/// Thread-safe — safe to call concurrently.
/// </summary>
public sealed class NodeTranslationTool : INodeTranslationTool
{
    private static readonly ActivitySource s_activitySource = new(WellKnownActivitySourceNames.Migration);

    private readonly NodeTranslationOptions _options;
    private readonly IReadOnlyList<(Regex Pattern, string Replacement)> _areaRules;
    private readonly IReadOnlyList<(Regex Pattern, string Replacement)> _iterationRules;
    private readonly IMigrationMetrics? _migrationMetrics;
    private readonly ILogger<NodeTranslationTool> _logger;

    public NodeTranslationTool(IOptions<NodeTranslationOptions> options, ILogger<NodeTranslationTool>? logger = null, IMigrationMetrics? migrationMetrics = null)
    {
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<NodeTranslationTool>.Instance;
        _migrationMetrics = migrationMetrics;
        _areaRules = CompileRules(_options.AreaPathMappings);
        _iterationRules = CompileRules(_options.IterationPathMappings);
    }

    /// <inheritdoc/>
    public bool IsEnabled => _options.Enabled;

    /// <inheritdoc/>
    public PathTranslation TranslatePath(
        string fieldName,
        string sourcePathValue,
        ProjectMapping context)
    {
        ArgumentNullException.ThrowIfNull(fieldName);
        ArgumentNullException.ThrowIfNull(sourcePathValue);
        ArgumentNullException.ThrowIfNull(context);

        using var activity = s_activitySource.StartActivity("nodes.translate");
        var path = sourcePathValue.Trim();

        var tags = new MetricsTagList
        {
            { WellKnownTagNames.Module, "Nodes" },
            { WellKnownTagNames.Operation, "nodes.translate" }
        };
        _migrationMetrics?.RecordNodeTranslateCount(tags);

        // Step 1 — Language override: normalise root segment
        path = ApplyLanguageOverride(fieldName, path, context);

        // Step 2 — Regex mapping rules (first match wins)
        var rules = IsAreaField(fieldName) ? _areaRules : _iterationRules;
        foreach (var (pattern, replacement) in rules)
        {
            if (pattern.IsMatch(path))
            {
                var mapped = pattern.Replace(path, replacement);
                using (DataClassificationScope.Begin(DataClassification.Customer))
                    _logger.LogTrace("[NodeTranslation] Path translated via map rule: {Source} → {Target}", path, mapped);
                _migrationMetrics?.RecordNodeTranslateMapHit(tags);
                return new PathTranslation(
                    TargetPath: mapped,
                    MatchedByMap: true,
                    MatchedByProjectSwap: false,
                    IsExternalPath: false);
            }
        }

        // Step 3 — Auto-swap: if path starts with source project name, replace with target
        if (path.StartsWith(context.SourceProjectName + "\\", StringComparison.OrdinalIgnoreCase)
            || string.Equals(path, context.SourceProjectName, StringComparison.OrdinalIgnoreCase))
        {
            var swapped = context.TargetProjectName + path[context.SourceProjectName.Length..];
            using (DataClassificationScope.Begin(DataClassification.Customer))
                _logger.LogTrace("[NodeTranslation] Path translated via auto-swap: {Source} → {Target}", path, swapped);
            _migrationMetrics?.RecordNodeTranslateAutoSwapHit(tags);
            return new PathTranslation(
                TargetPath: swapped,
                MatchedByMap: false,
                MatchedByProjectSwap: true,
                IsExternalPath: false);
        }

        // Step 4 — External path: not anchored in source project — pass through
        using (DataClassificationScope.Begin(DataClassification.Customer))
            _logger.LogWarning("[NodeTranslation] Path is external (not anchored in source project): {Path}", path);
        _migrationMetrics?.RecordNodeTranslateExternal(tags);
        return new PathTranslation(
            TargetPath: path,
            MatchedByMap: false,
            MatchedByProjectSwap: false,
            IsExternalPath: true);
    }

    // --- Helpers ---

    private static IReadOnlyList<(Regex, string)> CompileRules(IReadOnlyList<NodeMapping> mappings)
    {
        var result = new List<(Regex, string)>(mappings.Count);
        foreach (var m in mappings)
        {
            var regex = new Regex(
                m.Match,
                RegexOptions.IgnoreCase | RegexOptions.NonBacktracking | RegexOptions.Compiled,
                TimeSpan.FromSeconds(5));
            result.Add((regex, m.Replacement));
        }
        return result;
    }

    private string ApplyLanguageOverride(string fieldName, string path, ProjectMapping context)
    {
        var languageOverride = IsAreaField(fieldName)
            ? _options.AreaLanguageOverride
            : _options.IterationLanguageOverride;

        if (string.IsNullOrEmpty(languageOverride)) return path;

        // Find the first backslash — the root segment ends there
        var firstSep = path.IndexOf('\\');
        if (firstSep < 0)
        {
            // Single-segment path: the whole thing is the root — replace it
            return languageOverride + "\\" + path;
        }

        // Replace root segment with normalised value
        return languageOverride + path[firstSep..];
    }

    private static bool IsAreaField(string fieldName)
        => string.Equals(fieldName, "System.AreaPath", StringComparison.OrdinalIgnoreCase);
}
#endif

