// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Options;

namespace DevOpsMigrationPlatform.Infrastructure.Simulated.Export;

/// <summary>
/// Simulated implementation of <see cref="IWorkItemCommentSourceFactory"/>.
/// Creates a <see cref="SimulatedWorkItemCommentSource"/> that generates
/// synthetic comments based on the generator configuration.
/// </summary>
public sealed class SimulatedWorkItemCommentSourceFactory : IWorkItemCommentSourceFactory
{
    private readonly bool _hasComments;

    public SimulatedWorkItemCommentSourceFactory(bool hasComments)
    {
        _hasComments = hasComments;
    }

    /// <inheritdoc/>
    public IWorkItemCommentSource Create(MigrationEndpointOptions endpoint, string project)
    {
        return new SimulatedWorkItemCommentSource(_hasComments);
    }
}
