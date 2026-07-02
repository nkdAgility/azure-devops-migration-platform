// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Collections.Generic;

namespace DevOpsMigrationPlatform.Abstractions.Agent.Tools;

/// <summary>
/// Single entry point for all identity resolution during import.
/// All modules that reference user identities MUST use this tool — no inline resolution.
/// <para>
/// Pure, stateless translation engine (ADR-0026, TC-M1): resolved identity maps are passed
/// in as data (<see cref="IdentityTranslationMap"/>). Package I/O and map ownership live
/// with the Identities orchestrator (<c>IIdentitiesOrchestrator.TranslationMap</c>).
/// </para>
/// </summary>
public interface IIdentityTranslationTool
{
    /// <summary>
    /// Whether the tool is enabled. When <c>false</c>, <see cref="Translate"/> returns the source identity unchanged.
    /// </summary>
    bool IsEnabled { get; }

    /// <summary>
    /// The configured default (fallback) target identity, or <c>null</c>/empty when none is configured.
    /// A <see cref="Translate"/> result equal to this value indicates the source identity was unresolved
    /// and fell back to the default — callers (e.g. team-member import) use this to skip rather than
    /// import under the wrong identity.
    /// </summary>
    string? DefaultIdentity { get; }

    /// <summary>
    /// Pure parser for the package artefacts that feed identity translation: builds an
    /// <see cref="IdentityTranslationMap"/> from the raw text of <c>descriptors.jsonl</c>,
    /// <c>mapping.json</c>, and <c>prepared-identities.json</c>. Malformed inputs are
    /// non-fatal and yield empty segments. The caller owns reading the artefacts.
    /// </summary>
    IdentityTranslationMap ParseTranslationInputs(
        string? descriptorsJsonl,
        string? mappingJson,
        string? preparedIdentitiesJson);

    /// <summary>
    /// Translates a source identity to a target identity (synchronous, pure — reads only
    /// <paramref name="map"/> and configuration). Resolution order: (1) explicit override →
    /// (2/3) Prepare-phase UPN/display-name match → (4) configured default → source pass-through.
    /// </summary>
    string Translate(string sourceIdentity, IdentityTranslationMap map);

    /// <summary>
    /// Computes the source identities in <paramref name="map"/> that have neither an explicit
    /// mapping override nor a Prepare-phase match. The caller (Identities orchestrator) owns
    /// persisting the result as <c>Identities/unresolved.json</c>.
    /// </summary>
    IReadOnlyList<string> ComputeUnresolved(IdentityTranslationMap map);
}

/// <summary>
/// Immutable identity-resolution data passed to <see cref="IIdentityTranslationTool"/> as data
/// (ADR-0026, TC-M1). All lookups are case-insensitive.
/// </summary>
public sealed class IdentityTranslationMap
{
    /// <summary>An empty map: every translation falls through to default/pass-through.</summary>
    public static readonly IdentityTranslationMap Empty = new(
        new Dictionary<string, string>(), new Dictionary<string, string>(), Array.Empty<string>());

    public IdentityTranslationMap(
        IReadOnlyDictionary<string, string> overrides,
        IReadOnlyDictionary<string, string> prepared,
        IEnumerable<string> allUniqueNames)
    {
        if (overrides is null) throw new ArgumentNullException(nameof(overrides));
        if (prepared is null) throw new ArgumentNullException(nameof(prepared));
        if (allUniqueNames is null) throw new ArgumentNullException(nameof(allUniqueNames));

        var o = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var kv in overrides) o[kv.Key] = kv.Value;
        Overrides = o;

        var p = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var kv in prepared) p[kv.Key] = kv.Value;
        Prepared = p;

        AllUniqueNames = new HashSet<string>(allUniqueNames, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Explicit overrides from <c>mapping.json</c> (resolution step 1).</summary>
    public IReadOnlyDictionary<string, string> Overrides { get; }

    /// <summary>Prepare-phase UPN/display-name matches (resolution steps 2–3).</summary>
    public IReadOnlyDictionary<string, string> Prepared { get; }

    /// <summary>All source unique names from <c>descriptors.jsonl</c> (unresolved reporting).</summary>
    public IReadOnlyCollection<string> AllUniqueNames { get; }
}
