// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Storage;
using DevOpsMigrationPlatform.Abstractions.Agent.Context;
using DevOpsMigrationPlatform.Abstractions.Agent.Modules;
using DevOpsMigrationPlatform.Abstractions.Agent.Tools;
using DevOpsMigrationPlatform.Abstractions.Options;
using DevOpsMigrationPlatform.Abstractions.Telemetry;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Tools.IdentityTranslation;

/// <summary>
/// Full <see cref="IIdentityTranslationTool"/> implementation.
/// Reads identity descriptors and mapping overrides from the package artefact store.
/// Thread-safe after initialization (all state set once in <see cref="InitializeAsync"/>).
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
    private readonly IPackageAccess _package;
    private readonly IIdentitiesOrchestrator? _orchestrator;
    private readonly string _organisation;
    private readonly string _project;

    private Dictionary<string, string> _overrides = new(StringComparer.OrdinalIgnoreCase);
    private HashSet<string> _allUniqueNames = new(StringComparer.OrdinalIgnoreCase);

    public bool IsEnabled => _options.Enabled;

    public IdentityTranslationTool(
        IOptions<IdentityTranslationOptions> options,
        ISourceEndpointInfo sourceEndpointInfo,
        ILogger<IdentityTranslationTool>? logger = null,
        IPackageAccess? package = null,
        IIdentitiesOrchestrator? orchestrator = null)
    {
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        if (sourceEndpointInfo is null) throw new ArgumentNullException(nameof(sourceEndpointInfo));
        _organisation = sourceEndpointInfo.OrganisationSlug;
        _project = sourceEndpointInfo.Project;
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<IdentityTranslationTool>.Instance;
        _package = package ?? throw new ArgumentNullException(nameof(package));
        _orchestrator = orchestrator;
    }

    /// <inheritdoc/>
    public async Task InitializeAsync(CancellationToken ct)
    {
        using var activity = s_activitySource.StartActivity("identities.lookup.initialize");
        // Read descriptors
        var descriptorsContent = await ReadPackageTextAsync("descriptors.jsonl", ct).ConfigureAwait(false);
        var allUniqueNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (descriptorsContent is not null)
        {
            foreach (var line in descriptorsContent.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries))
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
                    {
                        var resolvedUniqueName = uniqueName!;
                        allUniqueNames.Add(resolvedUniqueName);
                    }
                }
                catch (JsonException) { }
            }
        }

        // Read mapping overrides
        var mappingContent = await ReadPackageTextAsync("mapping.json", ct).ConfigureAwait(false);
        var overrides = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (!string.IsNullOrWhiteSpace(mappingContent))
        {
            try
            {
                var mappingJson = mappingContent!;
                var raw = JsonSerializer.Deserialize<Dictionary<string, string>>(mappingJson, s_jsonOptions);
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

        _allUniqueNames = allUniqueNames;
        _overrides = overrides;

        _logger.LogInformation(
            "[IdentityLookup] Initialized with {DescriptorCount} descriptors, {MappingCount} mapping overrides.",
            _allUniqueNames.Count, _overrides.Count);
        activity?.SetTag("identities.descriptor.count", _allUniqueNames.Count);
        activity?.SetTag("identities.mapping.count", _overrides.Count);
    }

    /// <inheritdoc/>
    public string Translate(string sourceIdentity)
    {
        using var activity = s_activitySource.StartActivity("identity.translate");
        if (!IsEnabled || string.IsNullOrWhiteSpace(sourceIdentity))
            return sourceIdentity;

        // Step 1: explicit override from mapping.json.
        if (_overrides.TryGetValue(sourceIdentity, out var mapped))
            return mapped;

        // Steps 2-3: cached Prepare-phase UPN/display-name match.
        var prepared = _orchestrator?.ResolvePrepared(sourceIdentity);
        if (!string.IsNullOrWhiteSpace(prepared))
            return prepared!;

        // Step 4: configured default (when set); otherwise source pass-through.
        if (!string.IsNullOrWhiteSpace(_options.DefaultIdentity))
        {
            _logger.LogInformation("[IdentityTranslation] '{Source}' unresolved — returning configured default.", sourceIdentity);
            return _options.DefaultIdentity!;
        }

        return sourceIdentity;
    }

    /// <inheritdoc/>
    public async Task WriteUnresolvedAsync(CancellationToken ct)
    {
        var unresolved = new List<string>();
        foreach (var uniqueName in _allUniqueNames)
        {
            if (!_overrides.ContainsKey(uniqueName))
                unresolved.Add(uniqueName);
        }

        if (unresolved.Count == 0) return;

        var content = JsonSerializer.Serialize(unresolved, s_jsonOptions);
        await WritePackageTextAsync("unresolved.json", content, ct).ConfigureAwait(false);

        _logger.LogWarning(
            "[IdentityLookup] {Count} identit{Suffix} have no explicit mapping — written to Identities/unresolved.json.",
            unresolved.Count, unresolved.Count == 1 ? "y" : "ies");
    }

    private async Task<string?> ReadPackageTextAsync(string relativePath, CancellationToken ct)
    {
        var payload = await _package.RequestContentAsync(
            new PackageContentContext(PackageContentKind.Artefact, Organisation: _organisation, Project: _project, Module: "Identities", Address: new RelativePathAddress(relativePath)),
            ct).ConfigureAwait(false);
        if (payload is null)
            return null;

        if (payload.Content.CanSeek)
            payload.Content.Position = 0;
        using var reader = new StreamReader(payload.Content, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, bufferSize: 1024, leaveOpen: false);
        return await reader.ReadToEndAsync().ConfigureAwait(false);
    }

    private async Task WritePackageTextAsync(string relativePath, string content, CancellationToken ct)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(content), writable: false);
        await _package.PersistContentAsync(
            new PackageContentContext(PackageContentKind.Artefact, Organisation: _organisation, Project: _project, Module: "Identities", Address: new RelativePathAddress(relativePath)),
            new PackagePayload(stream, "application/json"),
            ct).ConfigureAwait(false);
    }
}
