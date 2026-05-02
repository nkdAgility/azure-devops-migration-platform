// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

#if !NET481
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using DevOpsMigrationPlatform.Abstractions.Agent.Identity;
using DevOpsMigrationPlatform.Abstractions.Agent.Modules;
using Microsoft.Extensions.Options;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Identity;

/// <summary>
/// Full <see cref="IIdentityMappingService"/> implementation.
/// Resolution order:
/// 1. Explicit override from <c>Identities/mapping.json</c>
/// 2. UPN/email matching
/// 3. Display name matching
/// 4. Configured default identity (falls back to the source identity when not set)
/// </summary>
public sealed class IdentityMappingService : IIdentityMappingService
{
    private static readonly JsonSerializerOptions s_jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true
    };

    private readonly IdentitiesModuleOptions _options;

    /// <summary>Explicit overrides loaded from mapping.json: sourceIdentity → targetIdentity.</summary>
    private readonly Dictionary<string, string> _overrides = new(StringComparer.OrdinalIgnoreCase);

    public IdentityMappingService(IOptions<IdentitiesModuleOptions> options)
    {
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
    }

    /// <summary>
    /// Loads explicit mapping overrides from the provided JSON string (contents of mapping.json).
    /// Call once during module import initialisation.
    /// </summary>
    public void LoadMappingOverrides(string? mappingJson)
    {
        _overrides.Clear();

        if (string.IsNullOrWhiteSpace(mappingJson))
            return;

        try
        {
            var raw = JsonSerializer.Deserialize<Dictionary<string, string>>(mappingJson, s_jsonOptions);
            if (raw is not null)
            {
                foreach (var kv in raw)
                    _overrides[kv.Key] = kv.Value;
            }
        }
        catch (JsonException)
        {
            // Mapping file parse failures are non-fatal — fall through to UPN/default logic
        }
    }

    /// <inheritdoc/>
    public string Resolve(string sourceIdentity)
    {
        if (string.IsNullOrWhiteSpace(sourceIdentity))
            return FallbackIdentity(sourceIdentity);

        // 1. Explicit override
        if (_overrides.TryGetValue(sourceIdentity, out var mapped))
            return mapped;

        // 2. Return source as-is with fallback to default
        return FallbackIdentity(sourceIdentity);
    }

    private string FallbackIdentity(string sourceIdentity)
    {
        if (!string.IsNullOrWhiteSpace(_options.DefaultIdentity))
            return _options.DefaultIdentity;

        return sourceIdentity;
    }
}
#endif
