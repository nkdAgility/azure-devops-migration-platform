// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Tests.Tools.NodeTranslation;

[TestClass]
public class SkipUnresolvableTests
{
    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public async Task ProcessAsync_SkipsRevision_WhenAreaPathUnresolvableAndSkipEnabled()
    {
        // Arrange — external area path, SkipOnUnresolvableArea = true
        var ctx = new SkipUnresolvableContext { SkipOnUnresolvableArea = true };
        ctx.SetPaths(@"OtherProject\Unknown", @"SourceProject\Sprint 1");

        // Act
        await ctx.RunProcessorAsync();

        // Assert — no exception; UpdateFieldsAsync not called
        Assert.IsNull(ctx.CaughtException, "Expected no exception.");
        Assert.IsFalse(ctx.UpdateFieldsWasCalled, "UpdateFieldsAsync should not be called.");
    }

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public async Task ProcessAsync_SkipsRevision_WhenIterationPathUnresolvableAndSkipEnabled()
    {
        // Arrange — external iteration path, SkipOnUnresolvableIteration = true
        var ctx = new SkipUnresolvableContext { SkipOnUnresolvableIteration = true };
        ctx.SetPaths(@"SourceProject\Team A", @"OtherProject\Unknown");

        // Act
        await ctx.RunProcessorAsync();

        // Assert
        Assert.IsNull(ctx.CaughtException, "Expected no exception.");
        Assert.IsFalse(ctx.UpdateFieldsWasCalled, "UpdateFieldsAsync should not be called.");
    }

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public async Task ProcessAsync_ThrowsInvalidOperation_WhenAreaPathUnresolvableAndSkipDisabled()
    {
        // Arrange — external area path, SkipOnUnresolvableArea = false (fail-fast)
        var ctx = new SkipUnresolvableContext { SkipOnUnresolvableArea = false };
        ctx.SetPaths(@"OtherProject\Unknown", @"SourceProject\Sprint 1");

        // Act
        await ctx.RunProcessorAsync();

        // Assert — exception thrown identifying area as unresolvable
        Assert.IsNotNull(ctx.CaughtException, "Expected an InvalidOperationException.");
        StringAssert.Contains(ctx.CaughtException!.Message.ToLowerInvariant(), "area",
            "Exception message should identify the field as area.");
    }

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public async Task ProcessAsync_SkipsRevision_WhenExternalAreaPath()
    {
        // Arrange — fully external project path, SkipOnUnresolvableArea = true
        var ctx = new SkipUnresolvableContext { SkipOnUnresolvableArea = true };
        ctx.SetPaths(@"ExternalProject\Node", @"SourceProject\Sprint 1");

        // Act
        await ctx.RunProcessorAsync();

        // Assert — skipped without exception
        Assert.IsNull(ctx.CaughtException, "Expected no exception for external path.");
        Assert.IsFalse(ctx.UpdateFieldsWasCalled, "Revision should be skipped.");
    }
}


