// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Organisations;
using DevOpsMigrationPlatform.Infrastructure.AzureDevOps.Platform.AzureDevOpsAccess;
using DevOpsMigrationPlatform.Infrastructure.AzureDevOps.WorkItems.Comments;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.TeamFoundation.WorkItemTracking.WebApi;
using Microsoft.TeamFoundation.WorkItemTracking.WebApi.Models;
using Microsoft.VisualStudio.Services.Common;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace DevOpsMigrationPlatform.Infrastructure.AzureDevOps.Tests.WorkItems;

/// <summary>
/// PR #222 review: the comments list holds only each comment's latest version, so an edit at t1
/// followed by another at t2 lost the t1 version. Edited comments are read with their history.
/// </summary>
[TestClass]
public class AzureDevOpsWorkItemCommentSourceVersionTests
{
    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public async Task GetCommentsAsync_EditedComment_YieldsEveryVersion()
    {
        var t1 = new DateTime(2026, 11, 17, 10, 0, 0, DateTimeKind.Utc);
        var t2 = t1.AddMinutes(5);
        var client = new Mock<WorkItemTrackingHttpClient>(new Uri("https://dev.azure.com/org"), new VssCredentials());
        client.Setup(c => c.GetCommentsAsync("P", 42, It.IsAny<int?>(), It.IsAny<string>(), It.IsAny<bool?>(), It.IsAny<CommentExpandOptions?>(), It.IsAny<CommentSortOrder?>(), It.IsAny<object>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(new CommentList { Comments = [new Comment { Id = 9, Version = 2, Text = "second", ModifiedDate = t2 }] });
        client.Setup(c => c.GetCommentVersionsAsync("P", 42, 9, It.IsAny<object>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(new List<CommentVersion>
              {
                  new() { Id = 9, Version = 1, Text = "first", ModifiedDate = t1 },
                  new() { Id = 9, Version = 2, Text = "second", ModifiedDate = t2 }
              });
        var factory = new Mock<IAzureDevOpsClientFactory>();
        factory.Setup(f => f.CreateWorkItemClientAsync(It.IsAny<OrganisationEndpoint>(), It.IsAny<CancellationToken>())).ReturnsAsync(client.Object);
        var sut = new AzureDevOpsWorkItemCommentSource(factory.Object, new OrganisationEndpoint(), "P", NullLogger<AzureDevOpsWorkItemCommentSource>.Instance);

        var texts = new List<string>();
        await foreach (var c in sut.GetCommentsAsync(42, includeDeleted: true, CancellationToken.None))
            texts.Add(c.Text);

        CollectionAssert.AreEqual(new[] { "first", "second" }, texts);
    }
}
