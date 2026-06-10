// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

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

    // ── Scenario: HTML embedded image is downloaded and URL rewritten ─────────

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public async Task ProcessHtmlAsync_WhenAdoImageUrl_DownloadsAndRewritesSrc()
    {
        var imageBytes = new byte[] { 0x89, 0x50, 0x4E, 0x47 }; // PNG magic
        var downloader = new Mock<IEmbeddedImageDownloader>(MockBehavior.Strict);
        downloader
            .Setup(d => d.TryDownloadAsync("https://dev.azure.com/org/proj/_apis/wit/attachments/abc123", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EmbeddedImageDownloadResult { Bytes = imageBytes, Extension = "png" });

        var sut = CreateSut(downloader);
        var html = """<p><img src="https://dev.azure.com/org/proj/_apis/wit/attachments/abc123"></p>""";

        var result = await sut.ProcessHtmlAsync(html, "WorkItems/rev/folder", CancellationToken.None);

        Assert.IsFalse(result.Contains("https://dev.azure.com"), "Original URL should be replaced.");
        Assert.IsTrue(result.Contains("image-"), "Rewritten src should contain local filename prefix.");
    }

    // ── Scenario: Duplicate image URLs within same revision deduplicate ────────

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public async Task ProcessHtmlAsync_WhenSameUrlTwice_DownloadsOnce()
    {
        var imageBytes = new byte[] { 1, 2, 3, 4 };
        var downloader = new Mock<IEmbeddedImageDownloader>();
        downloader
            .Setup(d => d.TryDownloadAsync("https://dev.azure.com/org/proj/_apis/wit/attachments/shared123", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EmbeddedImageDownloadResult { Bytes = imageBytes, Extension = "png" });

        var sut = CreateSut(downloader);
        var html = """<p><img src="https://dev.azure.com/org/proj/_apis/wit/attachments/shared123"><img src="https://dev.azure.com/org/proj/_apis/wit/attachments/shared123"></p>""";

        await sut.ProcessHtmlAsync(html, "WorkItems/rev/folder", CancellationToken.None);

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
    public async Task ProcessHtmlAsync_WhenDownloaderReturnsNull_PreservesOriginalUrl()
    {
        var downloader = new Mock<IEmbeddedImageDownloader>();
        downloader
            .Setup(d => d.TryDownloadAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((EmbeddedImageDownloadResult?)null);

        var sut = CreateSut(downloader);
        var html = """<p><img src="https://example.com/external-image.png"></p>""";

        var result = await sut.ProcessHtmlAsync(html, "WorkItems/rev/folder", CancellationToken.None);

        Assert.IsTrue(result.Contains("https://example.com/external-image.png"), "External URL should be preserved.");
    }

    // ── Scenario: Inaccessible image (HTTP 404) is preserved and export continues

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public async Task ProcessHtmlAsync_When404_PreservesUrlAndContinues()
    {
        var downloader = new Mock<IEmbeddedImageDownloader>();
        downloader
            .Setup(d => d.TryDownloadAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((EmbeddedImageDownloadResult?)null);

        var sut = CreateSut(downloader);
        var html = """<p><img src="https://dev.azure.com/org/proj/_apis/wit/attachments/deleted404"></p>""";

        // Should not throw
        var result = await sut.ProcessHtmlAsync(html, "WorkItems/rev/folder", CancellationToken.None);

        Assert.IsTrue(result.Contains("deleted404"), "URL should be preserved when download fails.");
    }

    // ── Scenario: Markdown embedded images are processed ─────────────────────

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public async Task ProcessMarkdownAsync_WhenMarkdownImageUrl_RewritesToLocalFilename()
    {
        var imageBytes = new byte[] { 0xFF, 0xD8, 0xFF }; // JPEG magic
        var downloader = new Mock<IEmbeddedImageDownloader>(MockBehavior.Strict);
        downloader
            .Setup(d => d.TryDownloadAsync("https://dev.azure.com/org/proj/_apis/wit/attachments/md567", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EmbeddedImageDownloadResult { Bytes = imageBytes, Extension = "jpg" });

        var sut = CreateSut(downloader);
        var markdown = "![alt text](https://dev.azure.com/org/proj/_apis/wit/attachments/md567)";

        var result = await sut.ProcessMarkdownAsync(markdown, "WorkItems/rev/folder", CancellationToken.None);

        Assert.IsFalse(result.Contains("https://dev.azure.com"), "Original URL should be replaced.");
        Assert.IsTrue(result.Contains("![alt text]"), "Alt text should be preserved.");
        Assert.IsTrue(result.Contains("image-"), "Rewritten markdown should reference local filename.");
    }
}
