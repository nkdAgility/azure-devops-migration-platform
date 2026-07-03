// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Agent.Tools;
using DevOpsMigrationPlatform.Abstractions.Options;
using DevOpsMigrationPlatform.Abstractions.Telemetry;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Tools.IdentityTranslation;

/// <summary>
/// Pure <see cref="IIdentityTranslationTool"/> implementation (ADR-0026, TC-M1).
/// Stateless: resolved maps are passed in as data. Package read/write and map ownership
/// moved to <c>IdentitiesOrchestrator</c>; the tool owns parsing and the resolution order only.
/// </summary>
public sealed class IdentityTranslationTool : IIdentityTranslationTool
{
    private static readonly ActivitySource s_activitySource = new(WellKnownActivitySourceNames.Migration);
    private static readonly JsonSerializerOptions s_jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly IdentityTranslationOptions _options;
    private readonly ILogger<IdentityTranslationTool> _logger;

    public bool IsEnabled => _options.Enabled;

    /// <inheritdoc/>
    public string? DefaultIdentity => _options.DefaultIdentity;

    public IdentityTranslationTool(
        IOptions<IdentityTranslationOptions> options,
        ILogger<IdentityTranslationTool>? logger = null)
    {
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<IdentityTranslationTool>.Instance;
    }

    /// <inheritdoc/>
    public IdentityTranslationMap ParseTranslationInputs(
        string? descriptorsJsonl,
        string? mappingJson,
        string? preparedIdentitiesJson)
    {
        using var activity = s_activitySource.StartActivity("identities.translation.parse");

        // Descriptors: collect all source unique names.
        var allUniqueNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (descriptorsJsonl is not null)
        {
            foreach (var line in descriptorsJsonl.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                try
                {
                    using var doc = JsonDocument.Parse(line);
                    var root = doc.RootElement;
                    var uniqueName = root.TryGetProperty("uniqueName", out var un) ? un.GetString()
                        : root.TryGetProperty("UniqueName", out var unU) ? unU.GetString()
                        : null;
                    if (!string.IsNullOrWhiteSpace(uniqueName))
                        allUniqueNames.Add(uniqueName!);
                }
                catch (JsonException) { }
            }
        }

        // Mapping overrides.
        var overrides = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(mappingJson))
        {
            try
            {
                var raw = JsonSerializer.Deserialize<Dictionary<string, string>>(mappingJson!, s_jsonOptions);
                if (raw is not null)
                {
                    foreach (var kv in raw)
                        overrides[kv.Key] = kv.Value;
                }
            }
            catch (JsonException)
            {
                // Mapping file parse failures are non-fatal — fall through to default logic
            }
        }

        // Prepared (auto-resolved) matches persisted by the Prepare phase.
        var prepared = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(preparedIdentitiesJson))
        {
            try
            {
                var raw = JsonSerializer.Deserialize<Dictionary<string, string>>(preparedIdentitiesJson!, s_jsonOptions);
                if (raw is not null)
                {
                    foreach (var kv in raw)
                        prepared[kv.Key] = kv.Value;
                }
            }
            catch (JsonException)
            {
                // Non-fatal — fall back to the orchestrator-provided prepared entries / default logic.
            }
        }

        _logger.LogInformation(
            "[IdentityTranslation] Parsed {DescriptorCount} descriptors, {MappingCount} mapping overrides, {PreparedCount} prepared matches.",
            allUniqueNames.Count, overrides.Count, prepared.Count);
        activity?.SetTag("identities.descriptor.count", allUniqueNames.Count);
        activity?.SetTag("identities.mapping.count", overrides.Count);
        activity?.SetTag("identities.prepared.count", prepared.Count);

        return new IdentityTranslationMap(overrides, prepared, allUniqueNames);
    }

    /// <inheritdoc/>
    public string Translate(string sourceIdentity, IdentityTranslationMap map)
    {
        if (map is null) throw new ArgumentNullException(nameof(map));

        using var activity = s_activitySource.StartActivity("identity.translate");
        if (!IsEnabled || string.IsNullOrWhiteSpace(sourceIdentity))
            return sourceIdentity;

        // Step 1: explicit override from mapping.json.
        if (map.Overrides.TryGetValue(sourceIdentity, out var mapped))
            return mapped;

        // Steps 2-3: Prepare-phase UPN/display-name match (persisted map merged with the
        // orchestrator's in-memory cache by the Identities orchestrator).
        if (map.Prepared.TryGetValue(sourceIdentity, out var preparedTarget) && !string.IsNullOrWhiteSpace(preparedTarget))
            return preparedTarget;

        // Step 4: configured default (when set); otherwise source pass-through.
        if (!string.IsNullOrWhiteSpace(_options.DefaultIdentity))
        {
            _logger.LogInformation("[IdentityTranslation] '{Source}' unresolved — returning configured default.", sourceIdentity);
            return _options.DefaultIdentity!;
        }

        return sourceIdentity;
    }

    /// <inheritdoc/>
    public IReadOnlyList<string> ComputeUnresolved(IdentityTranslationMap map)
    {
        if (map is null) throw new ArgumentNullException(nameof(map));

        var unresolved = new List<string>();
        foreach (var uniqueName in map.AllUniqueNames)
        {
            // An identity is resolved if it has an explicit mapping.json override OR was
            // auto-resolved by the Prepare phase (UPN/display-name match). Excluding the
            // latter prevents successfully translated identities being reported as failures.
            if (!map.Overrides.ContainsKey(uniqueName) && !map.Prepared.ContainsKey(uniqueName))
                unresolved.Add(uniqueName);
        }

        return unresolved;
    }
}
