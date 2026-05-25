// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Collections.Generic;
using DevOpsMigrationPlatform.Abstractions.Agent.ProjectLifecycle;
using DevOpsMigrationPlatform.Infrastructure.Agent.ProjectLifecycle;
using DevOpsMigrationPlatform.Infrastructure.Simulated.ProjectLifecycle;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DevOpsMigrationPlatform.CLI.Migration.Tests.TestUtilities;

[TestClass]
public sealed class SystemTestContextTests
{
    [TestMethod]
    public async Task SetupLifecycleAsync_BindsExecutionProjectName()
    {
        var context = new SystemTestContext(
            "LifecycleContextTest",
            new SystemTestConfiguration());

        var eligibility = new LifecycleEligibilityFlag
        {
            IsEnabled = true,
            Connectors = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Simulated" },
            NamePrefix = "ctx"
        };

        var lifecycle = CreateLifecycleService();
        var enabled = await context.SetupLifecycleAsync(lifecycle, "Simulated", eligibility);

        Assert.IsTrue(enabled);
        Assert.IsFalse(string.IsNullOrWhiteSpace(context.ExecutionProjectName));
        Assert.IsNotNull(context.LifecycleRecord);
    }

    [TestMethod]
    public async Task TeardownLifecycleAsync_AttemptsCleanupAfterSetup()
    {
        var context = new SystemTestContext(
            "LifecycleTeardownTest",
            new SystemTestConfiguration());
        var lifecycle = CreateLifecycleService();
        var eligibility = new LifecycleEligibilityFlag
        {
            IsEnabled = true,
            Connectors = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Simulated" }
        };

        _ = await context.SetupLifecycleAsync(lifecycle, "Simulated", eligibility);
        await context.TeardownLifecycleAsync(lifecycle);

        Assert.IsNotNull(context.LifecycleRecord);
        Assert.AreEqual(ProjectLifecycleTeardownResult.Succeeded, context.LifecycleRecord.TeardownResult);
    }

    [TestMethod]
    public async Task CleanupIsAttempted_WhenExecutionThrowsAfterSetup()
    {
        var context = new SystemTestContext(
            "LifecycleFailureCleanupTest",
            new SystemTestConfiguration());
        var lifecycle = CreateLifecycleService();
        var eligibility = new LifecycleEligibilityFlag
        {
            IsEnabled = true,
            Connectors = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Simulated" }
        };

        try
        {
            _ = await context.SetupLifecycleAsync(lifecycle, "Simulated", eligibility);
            throw new InvalidOperationException("simulated test failure");
        }
        catch (InvalidOperationException)
        {
            // expected for this test
        }
        finally
        {
            await context.TeardownLifecycleAsync(lifecycle);
        }

        Assert.IsNotNull(context.LifecycleRecord);
        Assert.AreEqual(ProjectLifecycleTeardownResult.Succeeded, context.LifecycleRecord.TeardownResult);
    }

    private static ProjectLifecycleService CreateLifecycleService()
    {
        return new ProjectLifecycleService(
            new ProjectLifecycleNameGenerator(),
            new SimulatedProjectLifecycleProvider(),
            NullLogger<ProjectLifecycleService>.Instance);
    }
}
