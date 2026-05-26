// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using DevOpsMigrationPlatform.Abstractions.Agent.ProjectLifecycle;
using DevOpsMigrationPlatform.Infrastructure.Agent.ProjectLifecycle;
using DevOpsMigrationPlatform.Infrastructure.Simulated.ProjectLifecycle;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DevOpsMigrationPlatform.CLI.Migration.Tests.ProjectLifecycle;

[TestClass]
public sealed class ProjectLifecycleVisibilityTests
{
    [TestMethod]
    public async Task ProgressEmitter_ContainsRunCorrelationFields()
    {
        var lifecycle = new ProjectLifecycleService(
            new ProjectLifecycleNameGenerator(),
            new SimulatedProjectLifecycleProvider(),
            NullLogger<ProjectLifecycleService>.Instance);
        var created = await lifecycle.CreateAsync(new ProjectLifecycleContext
        {
            RunId = "vis-run-1",
            ConnectorType = "Simulated",
            NamePrefix = "vis"
        });
        var tornDown = await lifecycle.TeardownAsync(created);

        var emitter = new ProjectLifecycleProgressEmitter(NullLogger<ProjectLifecycleProgressEmitter>.Instance);
        emitter.Emit(tornDown);

        Assert.AreEqual("vis-run-1", tornDown.RunId);
        Assert.AreEqual("Simulated", tornDown.ConnectorType);
        Assert.IsFalse(string.IsNullOrWhiteSpace(tornDown.ProjectName));
    }
}
