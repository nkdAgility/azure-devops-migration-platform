// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Agent.Attachments;
using DevOpsMigrationPlatform.Abstractions.Agent.Export;
using DevOpsMigrationPlatform.Abstractions.Agent.WorkItems;
using DevOpsMigrationPlatform.Abstractions.Options;
using DevOpsMigrationPlatform.Abstractions.Storage;
using DevOpsMigrationPlatform.Infrastructure.Agent.WorkItems.Extensions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace DevOpsMigrationPlatform.Infrastructure.Tests.Import;

/// <summary>
/// Martin, 2026-11-17: images that appear only in edited comments must be exported (#154).
/// The Comments extension captures them beside comment.json and the import re-hosts them.
/// </summary>
[TestClass]
public class CommentEmbeddedImagesTests
{
    private const string ImageUrl = "https://dev.azure.com/source-org/_apis/wit/attachments/img1";

    private static async IAsyncEnumerable<WorkItemComment> Comments(WorkItemComment c, [EnumeratorCancellation] CancellationToken ct = default)
    {
        yield return c;
        await Task.Yield();
    }

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public async Task ExportAsync_EditedCommentWithImage_CapturesTheImageBesideTheComment()
    {
        var changed = new DateTime(2026, 11, 17, 10, 0, 0, DateTimeKind.Utc);
        var comment = new WorkItemComment { CommentId = "1", Text = $"<img src=\"{ImageUrl}\">", ModifiedDate = changed };
        var source = new Mock<IWorkItemCommentSource>();
        source.Setup(s => s.GetCommentsAsync(7, true, It.IsAny<CancellationToken>())).Returns((int _, bool _, CancellationToken ct) => Comments(comment, ct));
        var factory = new Mock<IWorkItemCommentSourceFactory>();
        factory.Setup(f => f.Create(It.IsAny<MigrationEndpointOptions>(), It.IsAny<string>())).Returns(source.Object);

        var capture = new Mock<IEmbeddedImageExportService>();
        IReadOnlyList<WorkItemField>? scanned = null;
        capture.Setup(c => c.CaptureFromFieldsAsync(It.IsAny<IReadOnlyList<WorkItemField>>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<IReadOnlyList<WorkItemField>, string, CancellationToken>((f, _, _) => scanned = f)
            .ReturnsAsync(new[] { new EmbeddedImageMetadata { OriginalUrl = ImageUrl, RelativePath = "img1.png", Extension = "png" } });

        var written = new Dictionary<string, string>();
        var package = new Mock<IPackageAccess>();
        package.Setup(p => p.PersistContentAsync(It.IsAny<PackageContentContext>(), It.IsAny<PackagePayload>(), It.IsAny<CancellationToken>()))
            .Callback<PackageContentContext, PackagePayload, CancellationToken>((c, p, _) =>
            {
                using var reader = new StreamReader(p.Content, Encoding.UTF8);
                written[c.Address!.RelativePath] = reader.ReadToEnd();
            })
            .Returns(ValueTask.CompletedTask);

        var sut = new CommentsWorkItemExtension(
            Microsoft.Extensions.Options.Options.Create(new CommentsExtensionOptions()),
            DevOpsMigrationPlatform.Infrastructure.Agent.Tests.TestUtilities.TestConnectorCapabilities.All,
            factory.Object);

        await sut.ExportAsync(new WorkItemRevisionContext
        {
            Package = package.Object,
            WorkItemId = 7,
            RevisionIndex = 3,
            FolderPath = "WorkItems/2026-11-17/rev-7-3/",
            SourceEndpoint = new AzureDevOpsEndpointOptions(),
            EmbeddedImageExport = capture.Object,
            Revision = new WorkItemRevision
            {
                WorkItemId = 7,
                RevisionIndex = 3,
                ChangedDate = changed,
                Fields = [new WorkItemField { ReferenceName = "System.CommentCount", Value = "1" }]
            }
        }, CancellationToken.None);

        Assert.IsTrue(scanned!.Any(f => f.Value?.Contains(ImageUrl) == true), "The comment text is scanned for images.");
        var images = written.Single(w => w.Key.EndsWith("comment-images.json")).Value;
        StringAssert.Contains(images, "img1.png");
    }

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public async Task ExportAsync_ImageFailsTemporarily_FailsTheRevisionSoItIsRetried()
    {
        var changed = new DateTime(2026, 11, 17, 10, 0, 0, DateTimeKind.Utc);
        var comment = new WorkItemComment { CommentId = "1", Text = $"<img src=\"{ImageUrl}\">", ModifiedDate = changed };
        var source = new Mock<IWorkItemCommentSource>();
        source.Setup(s => s.GetCommentsAsync(7, true, It.IsAny<CancellationToken>())).Returns((int _, bool _, CancellationToken ct) => Comments(comment, ct));
        var factory = new Mock<IWorkItemCommentSourceFactory>();
        factory.Setup(f => f.Create(It.IsAny<MigrationEndpointOptions>(), It.IsAny<string>())).Returns(source.Object);
        var capture = new Mock<IEmbeddedImageExportService>();
        capture.Setup(c => c.CaptureFromFieldsAsync(It.IsAny<IReadOnlyList<WorkItemField>>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new EmbeddedImageDownloadException("HTTP 503"));

        var sut = new CommentsWorkItemExtension(
            Microsoft.Extensions.Options.Options.Create(new CommentsExtensionOptions()),
            DevOpsMigrationPlatform.Infrastructure.Agent.Tests.TestUtilities.TestConnectorCapabilities.All,
            factory.Object);

        await Assert.ThrowsExactlyAsync<EmbeddedImageDownloadException>(() => sut.ExportAsync(new WorkItemRevisionContext
        {
            Package = Mock.Of<IPackageAccess>(),
            WorkItemId = 7,
            RevisionIndex = 3,
            FolderPath = "WorkItems/2026-11-17/rev-7-3/",
            SourceEndpoint = new AzureDevOpsEndpointOptions(),
            EmbeddedImageExport = capture.Object,
            Revision = new WorkItemRevision
            {
                WorkItemId = 7,
                RevisionIndex = 3,
                ChangedDate = changed,
                Fields = [new WorkItemField { ReferenceName = "System.CommentCount", Value = "1" }]
            }
        }, CancellationToken.None));
    }

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public async Task ExportAsync_PackageWriteFails_FailsTheRevisionSoItIsRetried()
    {
        // PR #222 review: a failed write of comment-images.json or comment.json must not be
        // logged and dropped, or the revision is recorded as exported without them.
        var changed = new DateTime(2026, 11, 17, 10, 0, 0, DateTimeKind.Utc);
        var comment = new WorkItemComment { CommentId = "1", Text = "plain edit", ModifiedDate = changed };
        var source = new Mock<IWorkItemCommentSource>();
        source.Setup(s => s.GetCommentsAsync(7, true, It.IsAny<CancellationToken>())).Returns((int _, bool _, CancellationToken ct) => Comments(comment, ct));
        var factory = new Mock<IWorkItemCommentSourceFactory>();
        factory.Setup(f => f.Create(It.IsAny<MigrationEndpointOptions>(), It.IsAny<string>())).Returns(source.Object);
        var package = new Mock<IPackageAccess>();
        package.Setup(p => p.PersistContentAsync(It.IsAny<PackageContentContext>(), It.IsAny<PackagePayload>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new IOException("disk full"));

        var sut = new CommentsWorkItemExtension(
            Microsoft.Extensions.Options.Options.Create(new CommentsExtensionOptions()),
            DevOpsMigrationPlatform.Infrastructure.Agent.Tests.TestUtilities.TestConnectorCapabilities.All,
            factory.Object);

        await Assert.ThrowsExactlyAsync<IOException>(() => sut.ExportAsync(new WorkItemRevisionContext
        {
            Package = package.Object,
            WorkItemId = 7,
            RevisionIndex = 3,
            FolderPath = "WorkItems/2026-11-17/rev-7-3/",
            SourceEndpoint = new AzureDevOpsEndpointOptions(),
            Revision = new WorkItemRevision
            {
                WorkItemId = 7,
                RevisionIndex = 3,
                ChangedDate = changed,
                Fields = [new WorkItemField { ReferenceName = "System.CommentCount", Value = "1" }]
            }
        }, CancellationToken.None));
    }

    private static async IAsyncEnumerable<WorkItemComment> FailsAfterFirst(WorkItemComment first, Exception failure)
    {
        yield return first;
        await Task.Yield();
        throw failure;
    }

    private static async Task ExportEdit(Exception fetchFailure)
    {
        var changed = new DateTime(2026, 11, 17, 10, 0, 0, DateTimeKind.Utc);
        var comment = new WorkItemComment { CommentId = "1", Text = "edit", ModifiedDate = changed };
        var source = new Mock<IWorkItemCommentSource>();
        source.Setup(s => s.GetCommentsAsync(7, true, It.IsAny<CancellationToken>())).Returns(FailsAfterFirst(comment, fetchFailure));
        var factory = new Mock<IWorkItemCommentSourceFactory>();
        factory.Setup(f => f.Create(It.IsAny<MigrationEndpointOptions>(), It.IsAny<string>())).Returns(source.Object);
        var sut = new CommentsWorkItemExtension(
            Microsoft.Extensions.Options.Options.Create(new CommentsExtensionOptions()),
            DevOpsMigrationPlatform.Infrastructure.Agent.Tests.TestUtilities.TestConnectorCapabilities.All,
            factory.Object);

        await sut.ExportAsync(new WorkItemRevisionContext
        {
            Package = new Mock<IPackageAccess>().Object,
            WorkItemId = 7,
            RevisionIndex = 3,
            FolderPath = "WorkItems/2026-11-17/rev-7-3/",
            SourceEndpoint = new AzureDevOpsEndpointOptions(),
            Revision = new WorkItemRevision
            {
                WorkItemId = 7,
                RevisionIndex = 3,
                ChangedDate = changed,
                Fields = [new WorkItemField { ReferenceName = "System.CommentCount", Value = "1" }]
            }
        }, CancellationToken.None);
    }

    // PR #222 review: with versions 1, 2 and 3 of one comment (edited at t2, then t3), each
    // edit revision exports its own version, not only the latest.
    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public async Task ExportAsync_CommentEditedTwice_EachEditRevisionExportsItsOwnVersion()
    {
        var t1 = new DateTime(2026, 11, 17, 10, 0, 0, DateTimeKind.Utc);
        var t2 = t1.AddMinutes(5);
        var t3 = t1.AddMinutes(10);
        WorkItemComment Version(int v, DateTime at) => new() { CommentId = "1", Version = v, Text = $"text v{v}", ModifiedDate = at };
        var source = new Mock<IWorkItemCommentSource>();
        source.Setup(s => s.GetCommentsAsync(7, true, It.IsAny<CancellationToken>()))
              .Returns((int _, bool _, CancellationToken ct) => Versions(ct, Version(1, t1), Version(2, t2), Version(3, t3)));
        var factory = new Mock<IWorkItemCommentSourceFactory>();
        factory.Setup(f => f.Create(It.IsAny<MigrationEndpointOptions>(), It.IsAny<string>())).Returns(source.Object);
        var written = new List<string>();
        var package = new Mock<IPackageAccess>();
        package.Setup(p => p.PersistContentAsync(It.IsAny<PackageContentContext>(), It.IsAny<PackagePayload>(), It.IsAny<CancellationToken>()))
               .Callback<PackageContentContext, PackagePayload, CancellationToken>((_, payload, _) =>
               {
                   using var reader = new StreamReader(payload.Content);
                   written.Add(reader.ReadToEnd());
               })
               .Returns(ValueTask.CompletedTask);
        var sut = new CommentsWorkItemExtension(
            Microsoft.Extensions.Options.Options.Create(new CommentsExtensionOptions()),
            DevOpsMigrationPlatform.Infrastructure.Agent.Tests.TestUtilities.TestConnectorCapabilities.All,
            factory.Object);

        foreach (var (rev, at) in new[] { (2, t2), (3, t3) })
            await sut.ExportAsync(new WorkItemRevisionContext
            {
                Package = package.Object,
                WorkItemId = 7,
                RevisionIndex = rev,
                FolderPath = $"WorkItems/2026-11-17/rev-7-{rev}/",
                SourceEndpoint = new AzureDevOpsEndpointOptions(),
                Revision = new WorkItemRevision
                {
                    WorkItemId = 7,
                    RevisionIndex = rev,
                    ChangedDate = at,
                    Fields = [new WorkItemField { ReferenceName = "System.CommentCount", Value = "1" }]
                }
            }, CancellationToken.None);

        // comment.json for revision 2 is written first, then revision 3's.
        var comments = written.Where(w => w.Contains("text v")).ToList();
        Assert.AreEqual(2, comments.Count);
        var rev2 = comments[0];
        var rev3 = comments[1];
        StringAssert.Contains(rev2, "text v2");
        Assert.IsFalse(rev2.Contains("text v3"), "Revision 2 must hold version 2, not the latest.");
        StringAssert.Contains(rev3, "text v3");
    }

    private static async IAsyncEnumerable<WorkItemComment> Versions(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct, params WorkItemComment[] versions)
    {
        foreach (var version in versions)
        {
            ct.ThrowIfCancellationRequested();
            yield return version;
        }
        await Task.CompletedTask;
    }

    // PR #222 review: a temporary failure on a later page discarded the matched edit and let
    // the revision commit, so a resume never fetched it again. It now fails the revision.
    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public async Task ExportAsync_CommentFetchFailsPartWay_FailsTheRevisionSoItIsRetried()
    {
        await Assert.ThrowsExactlyAsync<HttpRequestException>(() => ExportEdit(new HttpRequestException("page 2: 503")));
    }

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public async Task ExportAsync_ConnectorReportsNoCommentsApi_SkipsCommentsAndCommits()
    {
        await ExportEdit(new DevOpsMigrationPlatform.Abstractions.Agent.Export.CommentsApiUnavailableException("comments API not served"));
    }

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public async Task ImportAsync_CommentWithCapturedImage_PostsTheRehostedUrl()
    {
        const string folder = "WorkItems/2026-11-17/rev-7-3";
        var texts = new Dictionary<string, string?>
        {
            [$"{folder}/comment.json"] = $$"""[{"commentId":"1","text":"<img src=\"{{ImageUrl}}\">"}]""",
            [$"{folder}/comment-images.json"] = $$"""[{"originalUrl":"{{ImageUrl}}","relativePath":"img1.png","extension":"png"}]"""
        };
        var target = new Mock<IWorkItemTarget>();
        string? posted = null;
        target.Setup(t => t.CreateCommentAsync(70, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<int, string, CancellationToken>((_, text, _) => posted = text)
            .Returns(Task.CompletedTask);

        var sut = new CommentsWorkItemExtension(
            Microsoft.Extensions.Options.Options.Create(new CommentsExtensionOptions()),
            DevOpsMigrationPlatform.Infrastructure.Agent.Tests.TestUtilities.TestConnectorCapabilities.All);

        await sut.ImportAsync(new WorkItemExtensionContext
        {
            Organisation = "o",
            ProjectName = "p",
            EntityId = "7",
            Package = Mock.Of<IPackageAccess>(),
            Revision = new WorkItemRevision { WorkItemId = 7, RevisionIndex = 3 },
            TargetWorkItemId = 70,
            FolderPath = folder,
            Target = target.Object,
            ReadTextAsync = (path, _) => Task.FromResult(texts.TryGetValue(path, out var t) ? t : null),
            RewriteEmbeddedImagesAsync = (text, images, _) =>
                Task.FromResult(text.Replace(images.Single().OriginalUrl, "https://dev.azure.com/target-org/_apis/wit/attachments/new"))
        }, CancellationToken.None);

        Assert.AreEqual("<img src=\"https://dev.azure.com/target-org/_apis/wit/attachments/new\">", posted);
    }
}
