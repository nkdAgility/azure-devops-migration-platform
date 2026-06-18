// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System.Threading;
using System.Threading.Tasks;

namespace DevOpsMigrationPlatform.Abstractions.Agent.ProjectLifecycle;

public interface IProjectLifecycleProvider
{
    Task CreateActionAsync(ProjectLifecycleContext context, string projectName, CancellationToken cancellationToken);

    Task TeardownActionAsync(ProjectLifecycleRecord record, CancellationToken cancellationToken);
}
