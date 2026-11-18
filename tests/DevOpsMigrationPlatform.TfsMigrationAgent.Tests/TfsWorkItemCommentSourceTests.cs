// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions.Agent;
using DevOpsMigrationPlatform.Infrastructure.TfsObjectModel.Teams;
using DevOpsMigrationPlatform.Infrastructure.TfsObjectModel.WorkItems.Comments;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.TeamFoundation.WorkItemTracking.WebApi.Models;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DevOpsMigrationPlatform.TfsMigrationAgent.Tests;

/// <summary>
/// #171 folded into #169 (Martin, 2026-11-17): comments are exported from TFS like ADO. Comment
/// edits and deletes come from the comments REST API, which TFS 2019+ and Azure DevOps serve.
/// </summary>
[TestClass]
public class TfsWorkItemCommentSourceTests
{
    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public async Task GetCommentsAsync_ReadsEveryPageAndMapsEachComment()
    {
        var modified = new DateTime(2026, 11, 17, 10, 0, 0, DateTimeKind.Utc);
        var pages = new Dictionary<string, CommentList>
        {
            [""] = new CommentList { Comments = [new Comment { Id = 1, Version = 2, Text = "first", ModifiedDate = modified }], ContinuationToken = "next" },
            ["next"] = new CommentList { Comments = [new Comment { Id = 2, Version = 1, Text = "second", IsDeleted = true, ModifiedDate = modified }] }
        };
        var requested = new List<string>();
        var sut = new TfsWorkItemCommentSource(
            (workItemId, token, includeDeleted, ct) =>
            {
                Assert.AreEqual(42, workItemId);
                Assert.IsTrue(includeDeleted);
                requested.Add(token ?? string.Empty);
                return Task.FromResult(pages[token ?? string.Empty]);
            },
            NullLogger<TfsWorkItemCommentSource>.Instance);

        var comments = new List<DevOpsMigrationPlatform.Abstractions.Agent.WorkItems.WorkItemComment>();
        await foreach (var c in sut.GetCommentsAsync(42, includeDeleted: true, CancellationToken.None))
            comments.Add(c);

        CollectionAssert.AreEqual(new[] { "", "next" }, requested);
        CollectionAssert.AreEqual(new[] { "1", "2" }, comments.Select(c => c.CommentId).ToArray());
        Assert.AreEqual("first", comments[0].Text);
        Assert.IsTrue(comments[1].IsDeleted);
        Assert.AreEqual(new DateTimeOffset(modified), comments[0].ModifiedDate);
    }

    // PR #222 review: the comments list holds only each comment's latest version, so an edit
    // at t1 followed by another at t2 lost the t1 version. Edited comments are read with their
    // history and every version is yielded, so each revision finds the version it changed.
    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public async Task GetCommentsAsync_EditedComment_YieldsEveryVersion()
    {
        var t1 = new DateTime(2026, 11, 17, 10, 0, 0, DateTimeKind.Utc);
        var t2 = t1.AddMinutes(5);
        var sut = new TfsWorkItemCommentSource(
            (_, _, _, _) => Task.FromResult(new CommentList { Comments = [new Comment { Id = 9, Version = 2, Text = "second", ModifiedDate = t2 }] }),
            NullLogger<TfsWorkItemCommentSource>.Instance,
            (workItemId, commentId, ct) =>
            {
                Assert.AreEqual(42, workItemId);
                Assert.AreEqual(9, commentId);
                return Task.FromResult<IReadOnlyList<CommentVersion>>(
                [
                    new CommentVersion { Id = 9, Version = 1, Text = "first", ModifiedDate = t1 },
                    new CommentVersion { Id = 9, Version = 2, Text = "second", ModifiedDate = t2 }
                ]);
            });

        var comments = new List<DevOpsMigrationPlatform.Abstractions.Agent.WorkItems.WorkItemComment>();
        await foreach (var c in sut.GetCommentsAsync(42, includeDeleted: true, CancellationToken.None))
            comments.Add(c);

        CollectionAssert.AreEqual(new[] { "first", "second" }, comments.Select(c => c.Text).ToArray());
        CollectionAssert.AreEqual(new[] { new DateTimeOffset(t1), new DateTimeOffset(t2) }, comments.Select(c => c.ModifiedDate).ToArray());
    }

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public void CapabilityProvider_DeclaresWorkItemComments()
    {
        var provider = new TfsConnectorCapabilityProvider();

        Assert.IsTrue(provider.Has(ConnectorCapability.WorkItemComments));
        Assert.IsFalse(provider.Has(ConnectorCapability.None));
    }
}
