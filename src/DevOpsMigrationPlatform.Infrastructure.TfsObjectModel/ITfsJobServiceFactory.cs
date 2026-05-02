// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using DevOpsMigrationPlatform.Abstractions.Options;

namespace DevOpsMigrationPlatform.Infrastructure.TfsObjectModel;

/// <summary>
/// Creates per-job TFS services from a <see cref="MigrationEndpointOptions"/>.
/// Extracted as an interface to enable unit testing of <c>TfsJobAgentWorker</c>
/// without requiring a live TFS server.
/// </summary>
public interface ITfsJobServiceFactory
{
    /// <summary>
    /// Creates a scoped set of TFS services for a single job.
    /// The caller MUST dispose the returned <see cref="TfsJobServices"/> after the job completes.
    /// </summary>
    TfsJobServices CreateForEndpoint(MigrationEndpointOptions endpoint);
}
