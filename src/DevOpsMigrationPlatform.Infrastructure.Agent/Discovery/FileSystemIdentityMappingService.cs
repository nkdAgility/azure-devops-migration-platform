// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Discovery;

/// <summary>
/// Resolves source identities to target identities using an operator-supplied mapping dictionary.
/// Falls back to a configured default identity when no mapping exists and records unmapped
/// identities so they can be flushed to Logs/ via <see cref="FlushWarningsAsync"/>.
/// </summary>
public class FileSystemIdentityMappingService : IIdentityMappingService
{
    private readonly IReadOnlyDictionary<string, string> _mappings;
    private readonly string _fallbackIdentity;
    private readonly IArtefactStore _store;
    private readonly List<string> _unmapped = new();

    public FileSystemIdentityMappingService(
        IReadOnlyDictionary<string, string> mappings,
        string fallbackIdentity,
        IArtefactStore store)
    {
        _mappings = mappings ?? throw new ArgumentNullException(nameof(mappings));
        _fallbackIdentity = fallbackIdentity ?? throw new ArgumentNullException(nameof(fallbackIdentity));
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    /// <inheritdoc/>
    public string Resolve(string sourceIdentity)
    {
        if (_mappings.TryGetValue(sourceIdentity, out var target))
            return target;

        if (!_unmapped.Contains(sourceIdentity))
            _unmapped.Add(sourceIdentity);

        return _fallbackIdentity;
    }

    /// <inheritdoc/>
    /// <remarks>No-op: <see cref="FileSystemIdentityMappingService"/> is initialised with mappings at construction time.</remarks>
    public void LoadMappingOverrides(string? mappingJson) { }

    /// <summary>
    /// All source identities that fell back to the default during this session.
    /// </summary>
    public IReadOnlyList<string> UnmappedIdentities => _unmapped.ToList();

    /// <summary>
    /// Writes one warning log entry per unmapped identity to <c>Logs/</c> via
    /// <see cref="IArtefactStore"/> and clears the unmapped list.
    /// </summary>
    public async Task FlushWarningsAsync(CancellationToken cancellationToken)
    {
        foreach (var identity in _unmapped)
        {
            var msg = $"WARN: No identity mapping for '{identity}'. Fell back to '{_fallbackIdentity}'.";
            var key = PackagePaths.IdentityWarning(identity);
            await _store.WriteAsync(key, msg, cancellationToken).ConfigureAwait(false);
        }
        _unmapped.Clear();
    }
}
