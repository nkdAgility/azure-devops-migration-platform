// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions.Options;
using DevOpsMigrationPlatform.Infrastructure.Simulated.Import;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DevOpsMigrationPlatform.Infrastructure.Simulated.Tests.Import;

[TestClass]
public sealed class SimulatedWorkItemImportTargetFactoryTests
{
    [TestMethod]
    public async Task CreateAsync_NoConfiguredTypes_FallsBackToDefaultKnownTypes()
    {
        var options = Options.Create(new SimulatedEndpointOptions());
        var factory = new SimulatedWorkItemImportTargetFactory(options);

        var target = await factory.CreateAsync(CancellationToken.None);

        Assert.IsTrue(await target.WorkItemTypeExistsAsync("Bug", CancellationToken.None));
        Assert.IsTrue(await target.WorkItemTypeExistsAsync("Task", CancellationToken.None));
    }
}
