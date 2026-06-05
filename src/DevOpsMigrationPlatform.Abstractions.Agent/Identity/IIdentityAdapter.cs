// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace DevOpsMigrationPlatform.Abstractions.Agent.Identity;

/// <summary>
/// Connector-specific Adapter that queries the live <b>target</b> tenant for identity
/// candidates during the Prepare phase only. Distinct from <c>IIdentitySource</c>
/// (which enumerates source identities at export time). Called solely by
/// <c>IIdentitiesOrchestrator.PrepareAsync</c> — never at import or translate time.
/// </summary>
/// <remarks>
/// Implementations must not throw on a missing or unsupported search capability: they
/// return an empty list and log a structured warning so resolution falls through to the
/// configured default (see the TFS adapter contract).
/// </remarks>
public interface IIdentityAdapter
{
    /// <summary>Returns target candidates whose UPN/email matches <paramref name="upn"/>.</summary>
    Task<IReadOnlyList<IdentityCandidate>> FindByUpnAsync(string upn, string projectName, CancellationToken ct);

    /// <summary>Returns target candidates whose display name matches <paramref name="displayName"/>.</summary>
    Task<IReadOnlyList<IdentityCandidate>> FindByDisplayNameAsync(string displayName, string projectName, CancellationToken ct);
}
