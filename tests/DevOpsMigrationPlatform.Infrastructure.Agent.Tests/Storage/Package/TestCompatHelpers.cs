// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using DevOpsMigrationPlatform.Abstractions.Jobs;
using DevOpsMigrationPlatform.Abstractions.Storage;
using DevOpsMigrationPlatform.Infrastructure.Storage.FileSystem;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Tests.Storage.Package;

internal sealed record TestPackageAddress(string RelativePath) : IPackageContentAddress;

internal static class ActivePackageTestFactory
{
    public static (ActivePackageAccess Package, ActivePackageState State) Create(InMemoryPackageAccess store, string jobId = "job-1", JobKind kind = JobKind.Export, ILogger<ActivePackageAccess>? logger = null)
        => Create(store.Root, jobId, kind, logger);

    public static (ActivePackageAccess Package, ActivePackageState State) Create(string packageRoot, string jobId = "job-1", JobKind kind = JobKind.Export, ILogger<ActivePackageAccess>? logger = null)
    {
        var state = new ActivePackageState
        {
            CurrentPackageUri = packageRoot,
            CurrentJob = new Job
            {
                JobId = jobId,
                Kind = kind,
                ConfigPayload = $"{{\"MigrationPlatform\":{{\"Package\":{{\"WorkingDirectory\":\"{packageRoot.Replace("\\", "\\\\")}\"}}}}}}"
            }
        };

        return (new ActivePackageAccess(state, new PackagePathRouter(), logger ?? NullLogger<ActivePackageAccess>.Instance), state);
    }
}
