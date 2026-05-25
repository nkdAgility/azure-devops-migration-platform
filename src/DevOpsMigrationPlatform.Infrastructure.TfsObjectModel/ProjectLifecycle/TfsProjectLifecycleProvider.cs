// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions.Agent.ProjectLifecycle;
using DevOpsMigrationPlatform.Infrastructure.Agent.ProjectLifecycle;

namespace DevOpsMigrationPlatform.Infrastructure.TfsObjectModel.ProjectLifecycle;

/// <summary>
/// Team Foundation Server-specific lifecycle actions.
/// </summary>
public sealed class TfsProjectLifecycleProvider : IProjectLifecycleProvider
{
    public Task CreateActionAsync(ProjectLifecycleContext context, string projectName, CancellationToken cancellationToken)
        => Task.CompletedTask;

    public Task TeardownActionAsync(ProjectLifecycleRecord record, CancellationToken cancellationToken)
        => Task.CompletedTask;
}
