// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using DevOpsMigrationPlatform.Infrastructure.Agent.Checkpointing;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Tests.Storage.Package;

[TestClass]
public sealed class PackageRuntimeBoundaryEnforcementTests
{
    private static readonly string[] GuardedRuntimePaths =
    {
        @"src\DevOpsMigrationPlatform.Infrastructure.Agent\Modules\WorkItemsModule.cs",
        @"src\DevOpsMigrationPlatform.Infrastructure.Agent\Export\WorkItemExportOrchestrator.cs",
        @"src\DevOpsMigrationPlatform.Infrastructure.Agent\WorkItems\WorkItemOrchestrator.cs",
        @"src\DevOpsMigrationPlatform.Infrastructure.Agent\WorkItems\RevisionFolderProcessor.cs",
        @"src\DevOpsMigrationPlatform.Infrastructure.Agent\Modules\DependencyOrchestrator.cs",
        @"src\DevOpsMigrationPlatform.Infrastructure.Agent\Modules\IdentitiesOrchestrator.cs",
        @"src\DevOpsMigrationPlatform.Infrastructure.Agent\Modules\NodesOrchestrator.cs",
        @"src\DevOpsMigrationPlatform.Infrastructure.Agent\Modules\TeamsOrchestrator.cs",
        @"src\DevOpsMigrationPlatform.Infrastructure.Agent\Modules\IdentitiesModule.cs",
        @"src\DevOpsMigrationPlatform.Infrastructure.Agent\Modules\NodesModule.cs",
        @"src\DevOpsMigrationPlatform.Infrastructure.Agent\Modules\TeamsModule.cs",
        @"src\DevOpsMigrationPlatform.Infrastructure.Agent\Context\JobExecutionPlanBuilder.cs",
        @"src\DevOpsMigrationPlatform.Infrastructure.Agent\Context\JobPlanExecutor.cs",
        @"src\DevOpsMigrationPlatform.MigrationAgent\JobAgentWorker.cs",
        @"src\DevOpsMigrationPlatform.TfsMigrationAgent\TfsJobAgentWorker.cs",
        @"src\DevOpsMigrationPlatform.Infrastructure.Agent\Checkpointing\CheckpointingService.cs",
        @"src\DevOpsMigrationPlatform.Infrastructure.Agent\Checkpointing\PhaseTrackingService.cs",
        @"src\DevOpsMigrationPlatform.Infrastructure.Agent\Telemetry\PackageProgressSink.cs",
        @"src\DevOpsMigrationPlatform.Infrastructure.Agent\Telemetry\PackageLoggerProvider.cs"
    };

    private static readonly Regex ForbiddenDirectStoreAccess = new(
        @"\b(?:_artefactStore|_stateStore|artefactStore|stateStore|store)\.(?:ReadAsync|WriteAsync|AppendAsync|ExistsAsync|EnumerateAsync|ReadBinaryAsync|WriteBinaryAsync)\(",
        RegexOptions.Compiled);

    [TestMethod]
    public void RuntimePackageFacingPaths_DoNotUseDirectStoreReadWriteApis()
    {
        var repoRoot = ResolveRepoRoot();
        var violations = new List<string>();

        foreach (var relativePath in GuardedRuntimePaths)
        {
            var fullPath = Path.Combine(repoRoot, relativePath);
            Assert.IsTrue(File.Exists(fullPath), $"Guarded path not found: {relativePath}");

            var content = File.ReadAllText(fullPath);
            foreach (Match match in ForbiddenDirectStoreAccess.Matches(content))
                violations.Add($"{relativePath}: {match.Value}");
        }

        if (violations.Count > 0)
        {
            Assert.Fail("Direct runtime store access detected:\n" + string.Join(Environment.NewLine, violations.Distinct(StringComparer.Ordinal)));
        }
    }

    private static string ResolveRepoRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "DevOpsMigrationPlatform.slnx")))
                return current.FullName;

            current = current.Parent;
        }

        throw new InvalidOperationException("Repository root could not be resolved.");
    }
}
