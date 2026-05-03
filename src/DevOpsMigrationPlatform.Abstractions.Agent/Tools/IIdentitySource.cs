// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System.Collections.Generic;
using System.Threading;
using DevOpsMigrationPlatform.Abstractions.Agent.Identity;

namespace DevOpsMigrationPlatform.Abstractions.Agent.Tools;

/// <summary>
/// Connector abstraction for enumerating project identities from a source system.
/// </summary>
public interface IIdentitySource
{
    /// <summary>
    /// Enumerates all user and group identity descriptors for the given project.
    /// </summary>
    IAsyncEnumerable<IdentityDescriptor> EnumerateIdentitiesAsync(
        string projectName,
        CancellationToken cancellationToken);
}
