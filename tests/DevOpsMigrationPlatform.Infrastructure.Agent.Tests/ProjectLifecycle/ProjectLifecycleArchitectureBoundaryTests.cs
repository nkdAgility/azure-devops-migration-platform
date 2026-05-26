// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Tests.ProjectLifecycle;

[TestClass]
public sealed class ProjectLifecycleArchitectureBoundaryTests
{
    [TestMethod]
    public void LifecycleTypes_AreScopedToAgentAndConnectorAssemblies()
    {
        var allowedPrefixes = new[]
        {
            "DevOpsMigrationPlatform.Abstractions.Agent.ProjectLifecycle",
            "DevOpsMigrationPlatform.Infrastructure.Agent.ProjectLifecycle",
            "DevOpsMigrationPlatform.Infrastructure.Simulated.ProjectLifecycle",
            "DevOpsMigrationPlatform.Infrastructure.AzureDevOps.ProjectLifecycle",
            "DevOpsMigrationPlatform.Infrastructure.TfsObjectModel.ProjectLifecycle",
            "DevOpsMigrationPlatform.Infrastructure.Agent.Tests.ProjectLifecycle"
        };

        var lifecycleTypes = AppDomain.CurrentDomain.GetAssemblies()
            .SelectMany(a => a.GetTypes())
            .Where(t => t.FullName is not null && t.FullName.Contains(".ProjectLifecycle.", StringComparison.Ordinal))
            .ToList();

        Assert.IsTrue(lifecycleTypes.Count > 0, "Expected lifecycle types to be present in loaded assemblies.");
        Assert.IsTrue(lifecycleTypes.All(t => allowedPrefixes.Any(prefix => t.FullName!.StartsWith(prefix, StringComparison.Ordinal))));
        Assert.IsFalse(lifecycleTypes.Any(t => t.FullName!.StartsWith("DevOpsMigrationPlatform.CLI.Migration", StringComparison.Ordinal)));
    }
}
