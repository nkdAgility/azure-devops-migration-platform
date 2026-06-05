// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions.Agent.Identity;

namespace DevOpsMigrationPlatform.Infrastructure.Simulated;

/// <summary>
/// Simulated <see cref="IIdentityAdapter"/> that resolves identities against a deterministic
/// in-memory "target tenant". The candidate set mirrors <see cref="SimulatedIdentitySource"/>
/// (same UPNs and display names) but with distinct target descriptors, so Prepare-phase
/// UPN and display-name matching resolve deterministically in tests and simulated runs.
/// </summary>
public sealed class SimulatedIdentityAdapter : IIdentityAdapter
{
    private static readonly IdentityCandidate[] s_targetCandidates = new[]
    {
        new IdentityCandidate("vstfs:///Target/Identity/alice", "alice@simulated.example.com", "Alice Smith"),
        new IdentityCandidate("vstfs:///Target/Identity/bob", "bob@simulated.example.com", "Bob Jones"),
        new IdentityCandidate("vstfs:///Target/Identity/carol", "carol@simulated.example.com", "Carol Taylor"),
        new IdentityCandidate("vstfs:///Target/Identity/dave", "dave@simulated.example.com", "Dave Brown"),
        new IdentityCandidate("vstfs:///Target/Identity/devs", "devs@simulated.example.com", "Developers"),
        new IdentityCandidate("vstfs:///Target/Identity/admins", "admins@simulated.example.com", "Administrators"),
    };

    /// <inheritdoc/>
    public Task<IReadOnlyList<IdentityCandidate>> FindByUpnAsync(string upn, string projectName, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var matches = new List<IdentityCandidate>();
        if (!string.IsNullOrWhiteSpace(upn))
        {
            foreach (var candidate in s_targetCandidates)
            {
                if (string.Equals(candidate.Upn, upn, StringComparison.OrdinalIgnoreCase))
                    matches.Add(candidate);
            }
        }

        return Task.FromResult<IReadOnlyList<IdentityCandidate>>(matches);
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<IdentityCandidate>> FindByDisplayNameAsync(string displayName, string projectName, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var matches = new List<IdentityCandidate>();
        if (!string.IsNullOrWhiteSpace(displayName))
        {
            foreach (var candidate in s_targetCandidates)
            {
                if (string.Equals(candidate.DisplayName, displayName, StringComparison.OrdinalIgnoreCase))
                    matches.Add(candidate);
            }
        }

        return Task.FromResult<IReadOnlyList<IdentityCandidate>>(matches);
    }
}
