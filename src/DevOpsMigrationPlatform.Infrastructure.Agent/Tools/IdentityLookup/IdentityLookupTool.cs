// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

#if !NET481
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
using DevOpsMigrationPlatform.Abstractions.Agent.Tools;
using DevOpsMigrationPlatform.Abstractions.Options;
using DevOpsMigrationPlatform.Abstractions.Telemetry;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Tools.IdentityLookup;

/// <summary>
/// Full <see cref="IIdentityLookupTool"/> implementation.
/// Reads identity descriptors and mapping overrides from the package artefact store.
/// Thread-safe after initialization (all state set once in <see cref="InitializeAsync"/>).
/// </summary>
public sealed class IdentityLookupTool : IIdentityLookupTool
{
    private static readonly ActivitySource s_activitySource = new(WellKnownActivitySourceNames.Migration);
    private static readonly JsonSerializerOptions s_jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly IdentityLookupOptions _options;
    private readonly ILogger<IdentityLookupTool> _logger;
    private readonly IPackageAccess _package;

    private Dictionary<string, string> _overrides = new(StringComparer.OrdinalIgnoreCase);
    private HashSet<string> _allUniqueNames = new(StringComparer.OrdinalIgnoreCase);

    public bool IsEnabled => _options.Enabled;

    public IdentityLookupTool(
        IOptions<IdentityLookupOptions> options,
        ILogger<IdentityLookupTool>? logger = null,
        IPackageAccess? package = null)
    {
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<IdentityLookupTool>.Instance;
        _package = package ?? throw new ArgumentNullException(nameof(package));
    }

    /// <inheritdoc/>
    public async Task InitializeAsync(CancellationToken ct)
    {
        using var activity = s_activitySource.StartActivity("identities.lookup.initialize");
        // Read descriptors
        var descriptorsContent = await ReadPackageTextAsync("Identities/descriptors.jsonl", ct).ConfigureAwait(false);
        var allUniqueNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (descriptorsContent is not null)
        {
            foreach (var line in descriptorsContent.Split('\n', StringSplitOptions.RemoveEmptyEntries))
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
                        allUniqueNames.Add(uniqueName);
                }
                catch (JsonException) { }
            }
        }

        // Read mapping overrides
        var mappingContent = await ReadPackageTextAsync("Identities/mapping.json", ct).ConfigureAwait(false);
        var overrides = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (!string.IsNullOrWhiteSpace(mappingContent))
        {
            try
            {
                var raw = JsonSerializer.Deserialize<Dictionary<string, string>>(mappingContent, s_jsonOptions);
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
    public string Resolve(string sourceIdentity)
    {
        using var activity = s_activitySource.StartActivity("identities.lookup.resolve");
        if (string.IsNullOrWhiteSpace(sourceIdentity))
            return sourceIdentity;

        if (_overrides.TryGetValue(sourceIdentity, out var mapped))
            return mapped;

        if (!string.IsNullOrWhiteSpace(_options.DefaultIdentity))
            return _options.DefaultIdentity;

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
        await WritePackageTextAsync("Identities/unresolved.json", content, ct).ConfigureAwait(false);

        _logger.LogWarning(
            "[IdentityLookup] {Count} identit{Suffix} have no explicit mapping — written to Identities/unresolved.json.",
            unresolved.Count, unresolved.Count == 1 ? "y" : "ies");
    }

    private async Task<string?> ReadPackageTextAsync(string relativePath, CancellationToken ct)
    {
        var payload = await _package.RequestContentAsync(
            new PackageContentContext(PackageContentKind.Artefact, SplitRouteSegments(relativePath)),
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
            new PackageContentContext(PackageContentKind.Artefact, SplitRouteSegments(relativePath)),
            new PackagePayload(stream, "application/json"),
            ct).ConfigureAwait(false);
    }

    private static IReadOnlyList<string> SplitRouteSegments(string relativePath)
        => relativePath
            .Replace('\\', '/')
            .Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
}
#endif
