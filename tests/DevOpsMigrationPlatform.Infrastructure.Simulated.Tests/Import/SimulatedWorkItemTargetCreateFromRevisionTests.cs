// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions.Agent.WorkItems;
using DevOpsMigrationPlatform.Infrastructure.Simulated.Import;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DevOpsMigrationPlatform.Infrastructure.Simulated.Tests.Import;

/// <summary>
/// #169: the Simulated connector implements create-from-revision (one source revision = one save).
/// </summary>
[TestClass]
public class SimulatedWorkItemTargetCreateFromRevisionTests
{
    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public async Task CreateFromRevisionAsync_CreatesTheItem_AndReadsEachAttachmentOnce()
    {
        var target = new SimulatedWorkItemTarget();
        var opened = 0;

        var result = await target.CreateFromRevisionAsync(
            "Task",
            [new WorkItemField { ReferenceName = "System.Title", Value = "E2E169" }],
            [],
            [],
            [],
            [new PendingAttachment("screenshot.png", _ => { opened++; return Task.FromResult<Stream?>(new MemoryStream(new byte[] { 1, 2 })); })],
            CancellationToken.None);

        Assert.IsTrue(result.IsNewlyCreated);
        Assert.IsTrue(await target.WorkItemExistsAsync(result.TargetWorkItemId, CancellationToken.None));
        Assert.AreEqual(1, opened);
    }

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public async Task CreateFromRevisionAsync_ReportsEachStoredAttachmentByPackagePath()
    {
        var target = new SimulatedWorkItemTarget();

        var result = await target.CreateFromRevisionAsync(
            "Task",
            [new WorkItemField { ReferenceName = "System.Title", Value = "E2E169" }],
            [],
            [],
            [],
            [new PendingAttachment("a.png", _ => Task.FromResult<Stream?>(new MemoryStream(new byte[] { 1 }))) { RelativePath = "attachments/a.png" }],
            CancellationToken.None);

        Assert.AreEqual(1, result.CreatedAttachments.Count);
        Assert.AreEqual("attachments/a.png", result.CreatedAttachments[0].RelativePath);
    }

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public async Task CreateFromRevisionAsync_SameFileNameOneMissing_ReportsOnlyTheStoredOne()
    {
        // PR #222 review: two attachments named a.png, only one with a binary; the missing one
        // must not get a mapping.
        var target = new SimulatedWorkItemTarget();

        var result = await target.CreateFromRevisionAsync(
            "Task",
            [new WorkItemField { ReferenceName = "System.Title", Value = "E2E169" }],
            [],
            [],
            [],
            [
                new PendingAttachment("a.png", _ => Task.FromResult<Stream?>(new MemoryStream(new byte[] { 1 }))) { RelativePath = "rev0/a.png" },
                new PendingAttachment("a.png", _ => Task.FromResult<Stream?>(null)) { RelativePath = "rev1/a.png" }
            ],
            CancellationToken.None);

        Assert.AreEqual("rev0/a.png", result.CreatedAttachments.Single().RelativePath);
        Assert.AreEqual($"simulated://{result.TargetWorkItemId}/a.png", result.CreatedAttachments[0].AttachmentId);
    }
}
