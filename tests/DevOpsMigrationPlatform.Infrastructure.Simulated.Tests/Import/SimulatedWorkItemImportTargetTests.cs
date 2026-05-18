// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Infrastructure.Simulated.Import;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DevOpsMigrationPlatform.Infrastructure.Simulated.Tests.Import;

[TestClass]
public sealed class SimulatedWorkItemImportTargetTests
{
    [TestMethod]
    public async Task CreateWorkItemAsync_AssignsSequentialIds()
    {
        var target = new SimulatedWorkItemImportTarget();

        var result1 = await target.CreateWorkItemAsync("User Story", Array.Empty<WorkItemField>(), CancellationToken.None);
        var result2 = await target.CreateWorkItemAsync("Bug", Array.Empty<WorkItemField>(), CancellationToken.None);
        var result3 = await target.CreateWorkItemAsync("Task", Array.Empty<WorkItemField>(), CancellationToken.None);

        Assert.AreEqual(1, result1.TargetWorkItemId);
        Assert.AreEqual(2, result2.TargetWorkItemId);
        Assert.AreEqual(3, result3.TargetWorkItemId);
    }

    // TODO: [test-validity] LOW VALUE — only tests IsNewlyCreated=true, a trivial flag assertion with no meaningful failure mode
    [TestMethod]
    public async Task CreateWorkItemAsync_SetsIsNewlyCreated()
    {
        var target = new SimulatedWorkItemImportTarget();
        var result = await target.CreateWorkItemAsync("Bug", Array.Empty<WorkItemField>(), CancellationToken.None);
        Assert.IsTrue(result.IsNewlyCreated);
    }

    [TestMethod]
    public async Task CreateWorkItemAsync_EmptyWorkItemType_ThrowsArgumentException()
    {
        var target = new SimulatedWorkItemImportTarget();
        await Assert.ThrowsExactlyAsync<ArgumentException>(
            () => target.CreateWorkItemAsync("", Array.Empty<WorkItemField>(), CancellationToken.None));
    }

    [TestMethod]
    public async Task UpdateFieldsAsync_InvalidId_ThrowsArgumentOutOfRangeException()
    {
        var target = new SimulatedWorkItemImportTarget();
        await Assert.ThrowsExactlyAsync<ArgumentOutOfRangeException>(
            () => target.UpdateFieldsAsync(0, Array.Empty<WorkItemField>(), CancellationToken.None));
    }

    [TestMethod]
    public async Task UpdateFieldsAsync_UnknownWorkItem_ThrowsInvalidOperationException()
    {
        var target = new SimulatedWorkItemImportTarget();
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => target.UpdateFieldsAsync(42, Array.Empty<WorkItemField>(), CancellationToken.None));
    }

    [TestMethod]
    public async Task WorkItemExistsAsync_ReturnsFalseBeforeCreate_ThenTrueAfterCreate()
    {
        var target = new SimulatedWorkItemImportTarget();

        var existsBefore = await target.WorkItemExistsAsync(1, CancellationToken.None);
        var created = await target.CreateWorkItemAsync("Bug", Array.Empty<WorkItemField>(), CancellationToken.None);
        var existsAfter = await target.WorkItemExistsAsync(created.TargetWorkItemId, CancellationToken.None);

        Assert.IsFalse(existsBefore);
        Assert.IsTrue(existsAfter);
    }

    [TestMethod]
    public async Task UploadAttachmentAsync_ReturnsDeterministicFakeId()
    {
        var target = new SimulatedWorkItemImportTarget();
        var id = await target.UploadAttachmentAsync(
            42, "test.pdf", Stream.Null, CancellationToken.None);

        Assert.IsTrue(id.StartsWith("simulated://42/"), $"Expected simulated:// URL, got: {id}");
        StringAssert.Contains(id, "test.pdf");
    }

    [TestMethod]
    public async Task WorkItemTypeExistsAsync_ReturnsTrue_ForKnownType()
    {
        var target = new SimulatedWorkItemImportTarget();

        var exists = await target.WorkItemTypeExistsAsync("Bug", CancellationToken.None);

        Assert.IsTrue(exists);
    }

    [TestMethod]
    public async Task WorkItemTypeExistsAsync_ReturnsFalse_ForUnknownType()
    {
        var target = new SimulatedWorkItemImportTarget();

        var exists = await target.WorkItemTypeExistsAsync("NotAType", CancellationToken.None);

        Assert.IsFalse(exists);
    }

}
