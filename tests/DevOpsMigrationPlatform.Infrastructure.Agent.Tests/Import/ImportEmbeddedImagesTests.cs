// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Agent.WorkItems;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace DevOpsMigrationPlatform.Infrastructure.Tests.Import;

[TestClass]
public class ImportEmbeddedImagesTests
{
    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public async Task ProcessAsync_UploadsImageAndRewritesUrl_WhenEmbeddedImagesExtensionEnabled()
    {
        // Arrange
        const string originalUrl = "https://source.example.com/api/attachments/img1.png";
        const string targetUrl = "https://target.example.com/attachments/img1.png";

        var ctx = new ImportEmbeddedImagesContext
        {
            OriginalUrl = originalUrl,
            TargetUrl = targetUrl,
            Extensions = new WorkItemsModuleExtensions()
        };

        ctx.RevisionJson = $$"""
        {
          "WorkItemId": 1,
          "RevisionIndex": 0,
          "Fields": [
            {"ReferenceName": "System.WorkItemType", "Value": "Task"},
            {"ReferenceName": "System.Description", "Value": "<img src=\"{{originalUrl}}\">"}
          ],
          "Attachments": [],
          "RelatedLinks": [],
          "ExternalLinks": [],
          "Hyperlinks": [],
          "EmbeddedImages": [
            {"OriginalUrl": "{{originalUrl}}", "RelativePath": "img1.png", "Extension": ".png", "Sha256": "abc", "Size": 100}
          ]
        }
        """;

        ctx.SetupMocks();
        var processor = ctx.BuildProcessor();

        // Act
        await processor.ProcessAsync(
            "WorkItems/2024-01-01/00000638000000000001-1-0",
            resumeAtStage: null,
            ctx.MockResolutionStrategy.Object,
            CancellationToken.None);

        // Assert — image uploaded
        ctx.MockTarget.Verify(
            t => t.UploadEmbeddedImageAsync("img1.png", It.IsAny<System.IO.Stream>(), It.IsAny<CancellationToken>()),
            Times.Once);

        // Assert — description field has target URL, not original URL
        ctx.MockTarget.Verify(
            t => t.ApplyRevisionAsync(
                It.IsAny<int>(),
                It.Is<IReadOnlyList<WorkItemField>>(f =>
                    f.Any(x => x.ReferenceName == "System.Description" &&
                               x.Value != null &&
                               x.Value.ToString()!.Contains(targetUrl) &&
                               !x.Value.ToString()!.Contains(originalUrl))),
                It.IsAny<IReadOnlyList<RelatedWorkItemLink>>(),
                It.IsAny<IReadOnlyList<ExternalWorkItemLink>>(),
                It.IsAny<IReadOnlyList<HyperlinkWorkItemLink>>(),
                It.IsAny<IReadOnlyList<AttachmentUploadResult>>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public async Task ProcessAsync_SkipsUploadAndPreservesOriginalUrl_WhenEmbeddedImagesExtensionDisabled()
    {
        // Arrange
        const string originalUrl = "https://source.example.com/api/attachments/img2.png";

        var ctx = new ImportEmbeddedImagesContext
        {
            OriginalUrl = originalUrl,
            Extensions = new WorkItemsModuleExtensions(),
            EmbeddedImagesOptions = new EmbeddedImagesExtensionOptionsConfig { Enabled = false }
        };

        ctx.RevisionJson = $$"""
        {
          "WorkItemId": 1,
          "RevisionIndex": 0,
          "Fields": [
            {"ReferenceName": "System.WorkItemType", "Value": "Task"},
            {"ReferenceName": "System.Description", "Value": "<img src=\"{{originalUrl}}\">"}
          ],
          "Attachments": [],
          "RelatedLinks": [],
          "ExternalLinks": [],
          "Hyperlinks": [],
          "EmbeddedImages": [
            {"OriginalUrl": "{{originalUrl}}", "RelativePath": "img2.png", "Extension": ".png", "Sha256": "def", "Size": 200}
          ]
        }
        """;

        ctx.SetupMocks();
        var processor = ctx.BuildProcessor();

        // Act
        await processor.ProcessAsync(
            "WorkItems/2024-01-01/00000638000000000001-1-0",
            resumeAtStage: null,
            ctx.MockResolutionStrategy.Object,
            CancellationToken.None);

        // Assert — no upload
        ctx.MockTarget.Verify(
            t => t.UploadEmbeddedImageAsync(It.IsAny<string>(), It.IsAny<System.IO.Stream>(), It.IsAny<CancellationToken>()),
            Times.Never);

        // Assert — original URL preserved in field value
        ctx.MockTarget.Verify(
            t => t.ApplyRevisionAsync(
                It.IsAny<int>(),
                It.Is<IReadOnlyList<WorkItemField>>(f =>
                    f.Any(x => x.ReferenceName == "System.Description" &&
                               x.Value != null &&
                               x.Value.ToString()!.Contains(originalUrl))),
                It.IsAny<IReadOnlyList<RelatedWorkItemLink>>(),
                It.IsAny<IReadOnlyList<ExternalWorkItemLink>>(),
                It.IsAny<IReadOnlyList<HyperlinkWorkItemLink>>(),
                It.IsAny<IReadOnlyList<AttachmentUploadResult>>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }
}


