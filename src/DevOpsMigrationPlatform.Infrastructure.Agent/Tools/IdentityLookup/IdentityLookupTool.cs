#if !NET481
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions.Agent.Storage;
using DevOpsMigrationPlatform.Abstractions.Agent.Tools;
using DevOpsMigrationPlatform.Abstractions.Options;
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
    private static readonly JsonSerializerOptions s_jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly IdentityLookupOptions _options;
    private readonly ILogger<IdentityLookupTool> _logger;

    private Dictionary<string, string> _overrides = new(StringComparer.OrdinalIgnoreCase);
    private HashSet<string> _allUniqueNames = new(StringComparer.OrdinalIgnoreCase);

    public bool IsEnabled => _options.Enabled;

    public IdentityLookupTool(IOptions<IdentityLookupOptions> options, ILogger<IdentityLookupTool>? logger = null)
    {
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<IdentityLookupTool>.Instance;
    }

    /// <inheritdoc/>
    public async Task InitializeAsync(IArtefactStore store, CancellationToken ct)
    {
        // Read descriptors
        var descriptorsContent = await store.ReadAsync("Identities/descriptors.jsonl", ct).ConfigureAwait(false);
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
        var mappingContent = await store.ReadAsync("Identities/mapping.json", ct).ConfigureAwait(false);
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
    }

    /// <inheritdoc/>
    public string Resolve(string sourceIdentity)
    {
        if (string.IsNullOrWhiteSpace(sourceIdentity))
            return sourceIdentity;

        if (_overrides.TryGetValue(sourceIdentity, out var mapped))
            return mapped;

        if (!string.IsNullOrWhiteSpace(_options.DefaultIdentity))
            return _options.DefaultIdentity;

        return sourceIdentity;
    }

    /// <inheritdoc/>
    public async Task WriteUnresolvedAsync(IArtefactStore store, CancellationToken ct)
    {
        var unresolved = new List<string>();
        foreach (var uniqueName in _allUniqueNames)
        {
            if (!_overrides.ContainsKey(uniqueName))
                unresolved.Add(uniqueName);
        }

        if (unresolved.Count == 0) return;

        var content = JsonSerializer.Serialize(unresolved, s_jsonOptions);
        await store.WriteAsync("Identities/unresolved.json", content, ct).ConfigureAwait(false);

        _logger.LogWarning(
            "[IdentityLookup] {Count} identit{Suffix} have no explicit mapping — written to Identities/unresolved.json.",
            unresolved.Count, unresolved.Count == 1 ? "y" : "ies");
    }
}
#endif
