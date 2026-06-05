// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions.Agent.Identity;
using Microsoft.Extensions.Logging;

namespace DevOpsMigrationPlatform.Infrastructure.TfsObjectModel.Identity;

/// <summary>
/// TFS implementation of <see cref="IIdentityAdapter"/> (net481, TFS agent project — the
/// project boundary is the runtime isolation seam; no <c>#if</c> guards).
/// </summary>
/// <remarks>
/// This is a documented TFS API-capability exemption, not a source-only claim: TFS capability
/// is implementation-defined and evolving (see docs/agent-hosting.md), but the TFS Identity
/// Service does not expose UPN or display-name search on the versions this platform targets.
/// Per FR-019 the reduced capability is modelled <b>explicitly in the contract result</b>:
/// every query returns an empty candidate list and logs a structured
/// <see cref="LogLevel.Warning"/>, causing the orchestrator to fall through to the configured
/// default identity. It MUST NOT throw.
/// </remarks>
public sealed class TfsIdentityAdapter : IIdentityAdapter
{
    private readonly ILogger<TfsIdentityAdapter> _logger;

    public TfsIdentityAdapter(ILogger<TfsIdentityAdapter> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<IdentityCandidate>> FindByUpnAsync(string upn, string projectName, CancellationToken ct)
    {
        _logger.LogWarning(
            "[Identities/TFS] UPN identity search is not supported by the TFS Identity Service; " +
            "returning no candidates — resolution will fall through to the configured default.");
        return Empty();
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<IdentityCandidate>> FindByDisplayNameAsync(string displayName, string projectName, CancellationToken ct)
    {
        _logger.LogWarning(
            "[Identities/TFS] Display-name identity search is not supported by the TFS Identity Service; " +
            "returning no candidates — resolution will fall through to the configured default.");
        return Empty();
    }

    private static Task<IReadOnlyList<IdentityCandidate>> Empty()
        => Task.FromResult<IReadOnlyList<IdentityCandidate>>(Array.Empty<IdentityCandidate>());
}
