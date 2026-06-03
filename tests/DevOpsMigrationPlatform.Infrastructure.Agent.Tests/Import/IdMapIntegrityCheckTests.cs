// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DevOpsMigrationPlatform.Infrastructure.Tests.Import;

[TestClass]
public class IdMapIntegrityCheckTests
{
    [TestCategory("UnitTests")]
    [TestMethod]
    public async Task CheckIntegrityAsync_ReturnsStaleEntries_WhenSomeMappedTargetsDoNotExist()
    {
        // Arrange
        var ctx = new IdMapIntegrityCheckContext();
        ctx.ConfiguredMappings.AddRange(new[]
        {
            new IdMapEntry { SourceId = 1, TargetId = 100 },
            new IdMapEntry { SourceId = 2, TargetId = 200 }
        });
        ctx.ExistingTargetIds.Add(200); // 100 does not exist
        ctx.SetupCheckIntegrity();

        // Act
        var result = await ctx.MockIdMapStore.Object.CheckIntegrityAsync(
            (targetId, ct) => Task.FromResult(ctx.ExistingTargetIds.Contains(targetId)),
            CancellationToken.None);

        // Assert — source 1 → target 100 is stale; source 2 → target 200 is valid
        Assert.IsNotNull(result);
        Assert.IsTrue(result.Count == 1, $"Expected 1 stale entry, got {result.Count}.");
        Assert.AreEqual(1, result[0].SourceId);
        Assert.AreEqual(100, result[0].TargetId);
    }

    [TestCategory("UnitTests")]
    [TestMethod]
    public async Task CheckIntegrityAsync_ReturnsEmpty_WhenAllMappedTargetsExist()
    {
        // Arrange
        var ctx = new IdMapIntegrityCheckContext();
        ctx.ConfiguredMappings.Add(new IdMapEntry { SourceId = 5, TargetId = 500 });
        ctx.ExistingTargetIds.Add(500);
        ctx.SetupCheckIntegrity();

        // Act
        var result = await ctx.MockIdMapStore.Object.CheckIntegrityAsync(
            (targetId, ct) => Task.FromResult(ctx.ExistingTargetIds.Contains(targetId)),
            CancellationToken.None);

        // Assert — no stale entries
        Assert.IsNotNull(result);
        Assert.AreEqual(0, result.Count, "Expected zero stale entries when all targets exist.");
    }
}

