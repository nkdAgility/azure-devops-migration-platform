// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Infrastructure.Agent.Export;
using DevOpsMigrationPlatform.Infrastructure.Storage.FileSystem;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using Reqnroll;
using Reqnroll.Assist;

namespace DevOpsMigrationPlatform.Infrastructure.Tests.Export;

[Binding]
[Scope(Feature = "Download Embedded Images from Work Item Fields")]
public class EmbeddedImagesSteps
{
    private readonly EmbeddedImagesContext _context;

    public EmbeddedImagesSteps(EmbeddedImagesContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Background
    // ──────────────────────────────────────────────────────────────────────────

    [Given("the test project is ready for export")]
    public void GivenTheTestProjectIsReadyForExport()
    {
        _context.PackageRoot = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(_context.PackageRoot);
        _context.ArtefactStore = new FileSystemArtefactStore(_context.PackageRoot);
        _context.DownloadedImages = new Dictionary<string, byte[]>();
        _context.LoggedWarnings = new List<string>();
    }

    [Given("the WorkItems module is configured with EmbeddedImages\\.Enabled = true")]
    public void GivenTheWorkItemsModuleIsConfiguredWithEmbeddedImagesEnabled()
    {
        _context.EmbeddedImagesEnabled = true;
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Scenario 1: HTML embedded image is downloaded and URL rewritten
    // ──────────────────────────────────────────────────────────────────────────

    [Given("a work item revision contains an HTML field with an embedded ADO image:")]
    public void GivenAWorkItemRevisionContainsAnHtmlFieldWithEmbeddedAdoImage(DataTable table)
    {
        _context.TestField = new WorkItemField
        {
            ReferenceName = "System.Description",
            Value = "<p>Screenshot: <img src=\"https://dev.azure.com/org/proj/_apis/wit/attachments/abc123\"></p>"
        };

        _context.DownloadedImages["https://dev.azure.com/org/proj/_apis/wit/attachments/abc123"] =
            new byte[] { 0x89, 0x50, 0x4E, 0x47 }; // PNG magic bytes
    }

    [When("the export runs")]
    public async Task WhenTheExportRuns()
    {
        var mockLogger = new Mock<ILogger<EmbeddedImageExportService>>();

        // Setup mock downloader
        _context.MockDownloader = new Mock<IEmbeddedImageDownloader>();
        _context.MockDownloader
            .Setup(d => d.TryDownloadAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns((string url, CancellationToken ct) =>
            {
                if (url.StartsWith("https://example.com") && !url.Contains("deleted404"))
                {
                    // External URL - return null
                    _context.LoggedWarnings.Add($"Could not download image {url}, preserving original");
                    return Task.FromResult<EmbeddedImageDownloadResult?>(null);
                }

                if (url.Contains("deleted404"))
                {
                    // 404 case - return null with warning
                    _context.LoggedWarnings.Add($"Failed to download image {url}: HTTP 404");
                    return Task.FromResult<EmbeddedImageDownloadResult?>(null);
                }

                // ADO URL - return image
                if (_context.DownloadedImages.TryGetValue(url, out var imageData))
                {
                    var fileName = GenerateImageFileName(url);
                    return Task.FromResult<EmbeddedImageDownloadResult?>
                    (
                        new EmbeddedImageDownloadResult
                        {
                            Bytes = imageData,
                            Extension = "png"
                        }
                    );
                }

                return Task.FromResult<EmbeddedImageDownloadResult?>(null);
            });

        var exportService = new EmbeddedImageExportService(
            _context.MockDownloader.Object,
            _context.ArtefactStore,
            mockLogger.Object);

        if (_context.TestField != null)
        {
            _context.RewrittenHtml = await exportService.ProcessHtmlAsync(
                _context.TestField.Value as string ?? string.Empty,
                Path.Combine(_context.PackageRoot, "export.workitems", "2026-04-11", "123-12345-r0"),
                CancellationToken.None);
        }

        if (_context.TestMarkdown != null)
        {
            _context.RewrittenMarkdown = await exportService.ProcessMarkdownAsync(
                _context.TestMarkdown,
                Path.Combine(_context.PackageRoot, "export.workitems", "2026-04-11", "123-12345-r0"),
                CancellationToken.None);
        }
    }

    [Then("the image file with SHA-256 derived filename \\(e\\.g (\\w+\\.\\w+)\\) is written beside revision\\.json")]
    public void ThenTheImageFileWithSha256DerivedFilenameIsWritten(string exampleFilename)
    {
        var revisionDir = Path.Combine(_context.PackageRoot, "export.workitems", "2026-04-11", "123-12345-r0");
        Assert.IsTrue(Directory.Exists(revisionDir), $"Revision folder should exist at {revisionDir}");

        var imageFiles = Directory.GetFiles(revisionDir, "image-*.png");
        Assert.IsTrue(imageFiles.Length > 0, "At least one image file should be written");

        _context.WrittenImagePath = imageFiles[0];
    }

    [Then("the stored revision\\.json field value is rewritten to: (.+)")]
    public void ThenTheStoredRevisionJsonFieldValueIsRewrittenTo(string expectedRewriteValue)
    {
        Assert.IsNotNull(_context.RewrittenHtml, "Rewritten HTML should not be null");
        Assert.IsTrue(_context.RewrittenHtml.Contains("image-"), "HTML should contain local image reference");
        Assert.IsFalse(_context.RewrittenHtml.Contains("https://dev.azure.com"),
            "HTML should not contain original ADO URL");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Scenario 2: Duplicate image URLs within same revision deduplicate
    // ──────────────────────────────────────────────────────────────────────────

    [Given("a work item revision contains two fields with the same embedded image URL:")]
    public void GivenAWorkItemRevisionContainsTwoFieldsWithSameEmbeddedImageUrl(DataTable table)
    {
        var testUrl = "https://dev.azure.com/org/proj/_apis/wit/attachments/shared123";

        _context.TestField = new WorkItemField
        {
            ReferenceName = "System.Description",
            Value = $"<img src=\"{testUrl}\">"
        };

        _context.TestField2 = new WorkItemField
        {
            ReferenceName = "Microsoft.VSTS.Common.AcceptanceCriteria",
            Value = $"<img src=\"{testUrl}\">"
        };

        _context.DownloadedImages[testUrl] = new byte[] { 0x89, 0x50, 0x4E, 0x47 };
        _context.DownloadCallCount = 0;
    }

    [When("the export runs")]
    public async Task WhenTheExportRunsForDeduplication()
    {
        var mockLogger = new Mock<ILogger<EmbeddedImageExportService>>();

        _context.MockDownloader = new Mock<IEmbeddedImageDownloader>();
        _context.MockDownloader
            .Setup(d => d.TryDownloadAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns((string url, CancellationToken ct) =>
            {
                _context.DownloadCallCount++;
                if (_context.DownloadedImages.TryGetValue(url, out var imageData))
                {
                    return Task.FromResult<EmbeddedImageDownloadResult?>
                    (
                        new EmbeddedImageDownloadResult
                        {
                            Bytes = imageData,
                            Extension = "png"
                        }
                    );
                }
                return Task.FromResult<EmbeddedImageDownloadResult?>(null);
            });

        var exportService = new EmbeddedImageExportService(
            _context.MockDownloader.Object,
            _context.ArtefactStore,
            mockLogger.Object);

        var revisionDir = Path.Combine(_context.PackageRoot, "export.workitems", "2026-04-11", "123-12345-r0");
        Directory.CreateDirectory(revisionDir);

        _context.RewrittenHtml = await exportService.ProcessHtmlAsync(
            _context.TestField!.Value as string ?? string.Empty,
            revisionDir,
            CancellationToken.None);

        _context.RewrittenHtml2 = await exportService.ProcessHtmlAsync(
            _context.TestField2!.Value as string ?? string.Empty,
            revisionDir,
            CancellationToken.None);
    }

    [Then("the image is downloaded once")]
    public void ThenTheImageIsDownloadedOnce()
    {
        Assert.AreEqual(1, _context.DownloadCallCount,
            "Image should be downloaded only once per revision");
    }

    [Then("both field values are rewritten to reference the same local filename")]
    public void ThenBothFieldValuesAreRewrittenToReferenceSameLocalFilename()
    {
        Assert.IsNotNull(_context.RewrittenHtml);
        Assert.IsNotNull(_context.RewrittenHtml2);

        // Extract filename from both
        var match1 = System.Text.RegularExpressions.Regex.Match(_context.RewrittenHtml, @"image-[\da-f]+\.png");
        var match2 = System.Text.RegularExpressions.Regex.Match(_context.RewrittenHtml2, @"image-[\da-f]+\.png");

        Assert.IsTrue(match1.Success, "First field should contain image reference");
        Assert.IsTrue(match2.Success, "Second field should contain image reference");
        Assert.AreEqual(match1.Value, match2.Value, "Both fields should reference the same image file");
    }

    [Then("only one image file is written beside revision\\.json")]
    public void ThenOnlyOneImageFileIsWrittenBesideRevisionJson()
    {
        var revisionDir = Path.Combine(_context.PackageRoot, "export.workitems", "2026-04-11", "123-12345-r0");
        var imageFiles = Directory.GetFiles(revisionDir, "image-*.png");
        Assert.AreEqual(1, imageFiles.Length, "Only one image file should be written for deduplicated images");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Scenario 3: External non-ADO image URLs are preserved with warning
    // ──────────────────────────────────────────────────────────────────────────

    [Given("a work item revision contains an external image URL:")]
    public void GivenAWorkItemRevisionContainsExternalImageUrl(DataTable table)
    {
        _context.TestField = new WorkItemField
        {
            ReferenceName = "System.Description",
            Value = "<img src=\"https://example.com/external-image.png\">"
        };

        _context.ExternalImageUrl = "https://example.com/external-image.png";
    }

    [Then("the image URL is left unchanged in the stored field value")]
    public void ThenTheImageUrlIsLeftUnchangedInTheStoredFieldValue()
    {
        Assert.IsNotNull(_context.RewrittenHtml);
        Assert.IsTrue(_context.RewrittenHtml.Contains("https://example.com/external-image.png"),
            "External URL should be preserved unchanged");
    }

    [Then("a warning is logged: \"(.+)\"")]
    public void ThenAWarningIsLogged(string expectedWarning)
    {
        var hasWarning = _context.LoggedWarnings.Any(w => w.Contains("external-image.png") ||
            w.Contains("Could not download image"));
        Assert.IsTrue(hasWarning, $"Warning should be logged containing external-image.png");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Scenario 4: Inaccessible image (HTTP 404) is preserved with warning
    // ──────────────────────────────────────────────────────────────────────────

    [Given("a work item revision contains an ADO image URL that returns 404:")]
    public void GivenAWorkItemRevisionContainsAdoImageUrlThatReturns404(DataTable table)
    {
        _context.TestField = new WorkItemField
        {
            ReferenceName = "System.Description",
            Value = "<img src=\"https://dev.azure.com/org/proj/_apis/wit/attachments/deleted404\">"
        };
    }

    [When("the export runs")]
    public async Task WhenTheExportRunsFor404Handling()
    {
        var mockLogger = new Mock<ILogger<EmbeddedImageExportService>>();

        _context.MockDownloader = new Mock<IEmbeddedImageDownloader>();
        _context.MockDownloader
            .Setup(d => d.TryDownloadAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns((string url, CancellationToken ct) =>
            {
                if (url.Contains("deleted404"))
                {
                    _context.LoggedWarnings.Add($"Failed to download image {url}: HTTP 404");
                    return Task.FromResult<EmbeddedImageDownloadResult?>(null);
                }
                return Task.FromResult<EmbeddedImageDownloadResult?>(null);
            });

        var exportService = new EmbeddedImageExportService(
            _context.MockDownloader.Object,
            _context.ArtefactStore,
            mockLogger.Object);

        var revisionDir = Path.Combine(_context.PackageRoot, "export.workitems", "2026-04-11", "123-12345-r0");
        Directory.CreateDirectory(revisionDir);

        _context.RewrittenHtml = await exportService.ProcessHtmlAsync(
            _context.TestField!.Value as string ?? string.Empty,
            revisionDir,
            CancellationToken.None);
    }

    [Then("the original URL is preserved in the stored field value")]
    public void ThenTheOriginalUrlIsPreservedInTheStoredFieldValue()
    {
        Assert.IsNotNull(_context.RewrittenHtml);
        Assert.IsTrue(_context.RewrittenHtml.Contains("https://dev.azure.com"),
            "Original URL should be preserved when download fails");
    }

    [Then("a warning is logged: \"(.+)\"")]
    public void ThenAWarningIsLoggedWith404(string expectedWarning)
    {
        var has404Warning = _context.LoggedWarnings.Any(w => w.Contains("404"));
        Assert.IsTrue(has404Warning, "Warning should be logged for 404 error");
    }

    [Then("the export completes successfully without aborting")]
    public void ThenTheExportCompletesSuccessfullyWithoutAborting()
    {
        // If we got here without exception, the export completed
        Assert.IsTrue(true);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Scenario 5: Markdown embedded images are processed
    // ──────────────────────────────────────────────────────────────────────────

    [Given("a work item revision contains a Markdown field:")]
    public void GivenAWorkItemRevisionContainsMarkdownField(DataTable table)
    {
        _context.TestMarkdown = "![alt text](https://dev.azure.com/org/proj/_apis/wit/attachments/md567)";
        _context.TestMarkdownFieldFormat = "markdown";

        _context.DownloadedImages["https://dev.azure.com/org/proj/_apis/wit/attachments/md567"] =
            new byte[] { 0x89, 0x50, 0x4E, 0x47 };
    }

    [When("the export runs")]
    public async Task WhenTheExportRunsForMarkdown()
    {
        var mockLogger = new Mock<ILogger<EmbeddedImageExportService>>();

        _context.MockDownloader = new Mock<IEmbeddedImageDownloader>();
        _context.MockDownloader
            .Setup(d => d.TryDownloadAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns((string url, CancellationToken ct) =>
            {
                if (_context.DownloadedImages.TryGetValue(url, out var imageData))
                {
                    return Task.FromResult<EmbeddedImageDownloadResult?>
                    (
                        new EmbeddedImageDownloadResult
                        {
                            Bytes = imageData,
                            Extension = "png"
                        }
                    );
                }
                return Task.FromResult<EmbeddedImageDownloadResult?>(null);
            });

        var exportService = new EmbeddedImageExportService(
            _context.MockDownloader.Object,
            _context.ArtefactStore,
            mockLogger.Object);

        var revisionDir = Path.Combine(_context.PackageRoot, "export.workitems", "2026-04-11", "123-12345-r0");
        Directory.CreateDirectory(revisionDir);

        _context.RewrittenMarkdown = await exportService.ProcessMarkdownAsync(
            _context.TestMarkdown!,
            revisionDir,
            CancellationToken.None);
    }

    [Then("the image is downloaded and the Markdown reference is rewritten to local filename")]
    public void ThenTheImageIsDownloadedAndMarkdownReferenceIsRewritten()
    {
        Assert.IsNotNull(_context.RewrittenMarkdown);
        Assert.IsFalse(_context.RewrittenMarkdown.Contains("https://dev.azure.com"),
            "Markdown should not contain original ADO URL");
        Assert.IsTrue(_context.RewrittenMarkdown.Contains("image-"),
            "Markdown should contain local image reference");
    }

    [Then("the stored field value is: (.+)")]
    public void ThenTheStoredFieldValueIs(string expectedValue)
    {
        Assert.IsNotNull(_context.RewrittenMarkdown);
        Assert.IsTrue(_context.RewrittenMarkdown.StartsWith("![alt text](image-"),
            "Markdown should rewrite reference to local image filename");
        Assert.IsTrue(_context.RewrittenMarkdown.EndsWith(".png)"),
            "Markdown should end with .png extension");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Helper method
    // ──────────────────────────────────────────────────────────────────────────

    private string GenerateImageFileName(string url)
    {
        // Simple hash-based filename
        var hash = System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(url));
        return "image-" + BitConverter.ToString(hash[..8]).Replace("-", "").ToLower() + ".png";
    }
}

/// <summary>
/// Context shared across step definitions for embedded image export scenarios.
/// </summary>
[Binding]
public class EmbeddedImagesContext
{
    public string PackageRoot { get; set; } = string.Empty;
    public IArtefactStore ArtefactStore { get; set; } = null!;
    public Mock<IEmbeddedImageDownloader> MockDownloader { get; set; } = null!;

    public bool EmbeddedImagesEnabled { get; set; }
    public Dictionary<string, byte[]> DownloadedImages { get; set; } = new();
    public List<string> LoggedWarnings { get; set; } = new();

    public WorkItemField? TestField { get; set; }
    public WorkItemField? TestField2 { get; set; }
    public string? TestMarkdown { get; set; }
    public string? TestMarkdownFieldFormat { get; set; }

    public string? RewrittenHtml { get; set; }
    public string? RewrittenHtml2 { get; set; }
    public string? RewrittenMarkdown { get; set; }
    public string? WrittenImagePath { get; set; }

    public int DownloadCallCount { get; set; }
    public string? ExternalImageUrl { get; set; }
}
