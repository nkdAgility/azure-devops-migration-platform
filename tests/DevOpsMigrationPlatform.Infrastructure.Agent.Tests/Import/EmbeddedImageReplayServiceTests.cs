// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Agent.Attachments;
using DevOpsMigrationPlatform.Abstractions.Agent.WorkItems;
using DevOpsMigrationPlatform.Infrastructure.Agent.Import;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace DevOpsMigrationPlatform.Infrastructure.Tests.Import;

[TestClass]
public class EmbeddedImageReplayServiceTests
{
    [TestMethod]
    public async Task RewriteFieldValuesAsync_RewritesOriginalUrls_WithUploadedTargetUrls()
    {
        var fields = new List<WorkItemField>
        {
            new()
            {
                ReferenceName = "System.Description",
                Value = "<p><img src=\"https://source.example/image.png\" /></p>"
            }
        };
        var images = new List<EmbeddedImageMetadata>
        {
            new()
            {
                OriginalUrl = "https://source.example/image.png",
                RelativePath = "img1.png"
            }
        };

        var target = new Mock<IWorkItemImportTarget>(MockBehavior.Strict);
        target
            .Setup(t => t.UploadEmbeddedImageAsync("img1.png", It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("https://target.example/image.png");

        var sut = new EmbeddedImageReplayService(target.Object, NullLogger<EmbeddedImageReplayService>.Instance);
        var rewritten = await sut.RewriteFieldValuesAsync(
            fields,
            images,
            "WorkItems/2026-01-01/00000000000000000042-42-3",
            (_, _) => Task.FromResult<Stream?>(new MemoryStream([1, 2, 3])),
            CancellationToken.None);

        Assert.AreEqual(1, rewritten.Count);
        Assert.AreEqual(
            "<p><img src=\"https://target.example/image.png\" /></p>",
            rewritten[0].Value);
        target.VerifyAll();
    }
}
