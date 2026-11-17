// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions.Agent.Attachments;
using DevOpsMigrationPlatform.Abstractions.Agent.Context;
using DevOpsMigrationPlatform.Abstractions.Storage;
using DevOpsMigrationPlatform.Infrastructure.Agent.Export;
using DevOpsMigrationPlatform.Infrastructure.Agent.Tests.TestUtilities;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Tests.Export;

[TestClass]
public class EmbeddedImageExportServiceTests
{
    private static EmbeddedImageExportService CreateSut(
        Mock<IEmbeddedImageDownloader> downloader,
        Mock<IPackageAccess>? package = null)
    {
        package ??= PackageTestFactory.CreateLooseMock();
        var endpoint = Mock.Of<ISourceEndpointInfo>(e =>
            e.OrganisationSlug == "org" && e.Project == "proj" && e.Url == "https://dev.azure.com/org");
        return new EmbeddedImageExportService(
            downloader.Object, package.Object, endpoint, NullLogger<EmbeddedImageExportService>.Instance);
    }

    // ── #169 / #154: capture images for the package in the form the import replays ──

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public async Task CaptureFromFieldsAsync_SourceImage_IsSavedAndDescribedForImport_FieldValueUnchanged()
    {
        var imageBytes = new byte[] { 0x89, 0x50, 0x4E, 0x47 };
        const string url = "https://dev.azure.com/org/proj/_apis/wit/attachments/abc123?fileName=e2e.png";
        var downloader = new Mock<IEmbeddedImageDownloader>(MockBehavior.Strict);
        downloader.Setup(d => d.TryDownloadAsync(url, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EmbeddedImageDownloadResult { Bytes = imageBytes, Extension = "png" });
        var package = PackageTestFactory.CreateLooseMock();
        var sut = CreateSut(downloader, package);
        var fields = new[]
        {
            new DevOpsMigrationPlatform.Abstractions.Agent.WorkItems.WorkItemField { ReferenceName = "System.Description", Value = $"<div>Story</div><img src=\"{url}\">" },
            new DevOpsMigrationPlatform.Abstractions.Agent.WorkItems.WorkItemField { ReferenceName = "System.History", Value = $"<img src=\"{url}\">" },
            new DevOpsMigrationPlatform.Abstractions.Agent.WorkItems.WorkItemField { ReferenceName = "System.Title", Value = "no images" }
        };

        var images = await sut.CaptureFromFieldsAsync(fields, "2026-11-16/00639-280-0", CancellationToken.None);

        var image = images.Single();
        Assert.AreEqual(url, image.OriginalUrl, "The import maps the original URL to the target URL.");
        StringAssert.StartsWith(image.RelativePath, "image-");
        StringAssert.EndsWith(image.RelativePath, ".png");
        Assert.AreEqual("png", image.Extension);
        Assert.AreEqual(4, image.Size);
        Assert.AreEqual(64, image.Sha256.Length);
        downloader.Verify(d => d.TryDownloadAsync(url, It.IsAny<CancellationToken>()), Times.Once, "A repeated image is downloaded once.");
        package.Verify(p => p.PersistContentStreamAsync(
            It.Is<PackageContentContext>(c => c.Address!.RelativePath == $"2026-11-16/00639-280-0/{image.RelativePath}"),
            It.IsAny<System.IO.Stream>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public async Task CaptureFromFieldsAsync_ImageThatCannotBeDownloaded_IsLeftOut()
    {
        var downloader = new Mock<IEmbeddedImageDownloader>();
        downloader.Setup(d => d.TryDownloadAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((EmbeddedImageDownloadResult?)null);
        var sut = CreateSut(downloader);

        var images = await sut.CaptureFromFieldsAsync(
            [new DevOpsMigrationPlatform.Abstractions.Agent.WorkItems.WorkItemField { ReferenceName = "System.Description", Value = "<img src=\"https://example.com/x.png\">" }],
            "f", CancellationToken.None);

        Assert.AreEqual(0, images.Count);
    }
    // ── Scenario: HTML embedded image is downloaded and URL rewritten ─────────

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public async Task ExportImagesFromHtmlAsync_WhenAdoImageUrl_DownloadsAndRewritesSrc()
    {
        var imageBytes = new byte[] { 0x89, 0x50, 0x4E, 0x47 }; // PNG magic
        var downloader = new Mock<IEmbeddedImageDownloader>(MockBehavior.Strict);
        downloader
            .Setup(d => d.TryDownloadAsync("https://dev.azure.com/org/proj/_apis/wit/attachments/abc123", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EmbeddedImageDownloadResult { Bytes = imageBytes, Extension = "png" });

        var sut = CreateSut(downloader);
        var html = """<p><img src="https://dev.azure.com/org/proj/_apis/wit/attachments/abc123"></p>""";

        var result = await sut.ExportImagesFromHtmlAsync(html, "WorkItems/rev/folder", CancellationToken.None);

        Assert.IsFalse(result.Contains("https://dev.azure.com"), "Original URL should be replaced.");
        Assert.IsTrue(result.Contains("image-"), "Rewritten src should contain local filename prefix.");
    }

    // ── Scenario: Duplicate image URLs within same revision deduplicate ────────

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public async Task ExportImagesFromHtmlAsync_WhenSameUrlTwice_DownloadsOnce()
    {
        var imageBytes = new byte[] { 1, 2, 3, 4 };
        var downloader = new Mock<IEmbeddedImageDownloader>();
        downloader
            .Setup(d => d.TryDownloadAsync("https://dev.azure.com/org/proj/_apis/wit/attachments/shared123", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EmbeddedImageDownloadResult { Bytes = imageBytes, Extension = "png" });

        var sut = CreateSut(downloader);
        var html = """<p><img src="https://dev.azure.com/org/proj/_apis/wit/attachments/shared123"><img src="https://dev.azure.com/org/proj/_apis/wit/attachments/shared123"></p>""";

        await sut.ExportImagesFromHtmlAsync(html, "WorkItems/rev/folder", CancellationToken.None);

        downloader.Verify(d => d.TryDownloadAsync(
            "https://dev.azure.com/org/proj/_apis/wit/attachments/shared123",
            It.IsAny<CancellationToken>()), Times.Once,
            "Image should be downloaded exactly once for the specific shared URL.");
        downloader.VerifyNoOtherCalls();
    }

    // ── Scenario: External non-ADO image URLs are preserved with warning ──────

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public async Task ExportImagesFromHtmlAsync_WhenDownloaderReturnsNull_PreservesOriginalUrl()
    {
        var downloader = new Mock<IEmbeddedImageDownloader>();
        downloader
            .Setup(d => d.TryDownloadAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((EmbeddedImageDownloadResult?)null);

        var sut = CreateSut(downloader);
        var html = """<p><img src="https://example.com/external-image.png"></p>""";

        var result = await sut.ExportImagesFromHtmlAsync(html, "WorkItems/rev/folder", CancellationToken.None);

        Assert.IsTrue(result.Contains("https://example.com/external-image.png"), "External URL should be preserved.");
    }

    // ── Scenario: Inaccessible image (HTTP 404) is preserved and export continues

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public async Task ExportImagesFromHtmlAsync_When404_PreservesUrlAndContinues()
    {
        var downloader = new Mock<IEmbeddedImageDownloader>();
        downloader
            .Setup(d => d.TryDownloadAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((EmbeddedImageDownloadResult?)null);

        var sut = CreateSut(downloader);
        var html = """<p><img src="https://dev.azure.com/org/proj/_apis/wit/attachments/deleted404"></p>""";

        // Should not throw
        var result = await sut.ExportImagesFromHtmlAsync(html, "WorkItems/rev/folder", CancellationToken.None);

        Assert.IsTrue(result.Contains("deleted404"), "URL should be preserved when download fails.");
    }

    // ── Scenario: Markdown embedded images are processed ─────────────────────

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public async Task ExportImagesFromMarkdownAsync_WhenMarkdownImageUrl_RewritesToLocalFilename()
    {
        var imageBytes = new byte[] { 0xFF, 0xD8, 0xFF }; // JPEG magic
        var downloader = new Mock<IEmbeddedImageDownloader>(MockBehavior.Strict);
        downloader
            .Setup(d => d.TryDownloadAsync("https://dev.azure.com/org/proj/_apis/wit/attachments/md567", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EmbeddedImageDownloadResult { Bytes = imageBytes, Extension = "jpg" });

        var sut = CreateSut(downloader);
        var markdown = "![alt text](https://dev.azure.com/org/proj/_apis/wit/attachments/md567)";

        var result = await sut.ExportImagesFromMarkdownAsync(markdown, "WorkItems/rev/folder", CancellationToken.None);

        Assert.IsFalse(result.Contains("https://dev.azure.com"), "Original URL should be replaced.");
        Assert.IsTrue(result.Contains("![alt text]"), "Alt text should be preserved.");
        Assert.IsTrue(result.Contains("image-"), "Rewritten markdown should reference local filename.");
    }
}
