using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using Reqnroll;

namespace DevOpsMigrationPlatform.Infrastructure.Tests.Import;

[Binding]
[Scope(Feature = "Import Embedded Image URL Rewriting")]
public class ImportEmbeddedImagesSteps
{
    private readonly ImportEmbeddedImagesContext _ctx;

    public ImportEmbeddedImagesSteps(ImportEmbeddedImagesContext ctx) => _ctx = ctx;

    // ── Background ────────────────────────────────────────────────────────────

    [Given("a valid migration package exists at the configured package root")]
    public void GivenAValidMigrationPackageExistsAtTheConfiguredPackageRoot() { }

    [Given("a revision folder contains embedded image files")]
    public void GivenARevisionFolderContainsEmbeddedImageFiles() { }

    // ── Scenario 1: images uploaded and URLs rewritten ────────────────────────

    [Given(@"a revision's ""embeddedImages"" list contains an entry with ""originalUrl"" and ""relativePath""")]
    public void GivenRevisionHasEmbeddedImagesEntry()
    {
        const string originalUrl = "https://source.example.com/api/attachments/img1.png";
        _ctx.OriginalUrl = originalUrl;
        _ctx.TargetUrl = "https://target.example.com/attachments/img1.png";

        _ctx.RevisionJson = $$"""
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
    }

    [Given("the EmbeddedImages extension is enabled")]
    public void GivenEmbeddedImagesExtensionIsEnabled()
    {
        _ctx.Extensions = new WorkItemsModuleExtensions(); // EmbeddedImages enabled by default
    }

    [When("the import processes that revision before applying fields \\(Stage B\\)")]
    public async Task WhenTheImportProcessesThatRevisionBeforeStageb()
    {
        _ctx.SetupMocks();
        var processor = _ctx.BuildProcessor();
        await processor.ProcessAsync(
            "WorkItems/2024-01-01/00000638000000000001-1-0",
            _ctx.Extensions,
            resumeAtStage: null,
            _ctx.MockResolutionStrategy.Object,
            CancellationToken.None);
    }

    [Then("the image binary is uploaded to the target via UploadEmbeddedImageAsync")]
    public void ThenImageBinaryIsUploadedViaUploadEmbeddedImageAsync()
    {
        _ctx.MockTarget.Verify(
            t => t.UploadEmbeddedImageAsync("img1.png", It.IsAny<System.IO.Stream>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Then(@"all occurrences of ""originalUrl"" in field HTML values are replaced with the returned target URL")]
    public void ThenOriginalUrlIsReplacedWithTargetUrl()
    {
        var expectedTarget = _ctx.TargetUrl!;
        var expectedOriginal = _ctx.OriginalUrl!;
        _ctx.MockTarget.Verify(
            t => t.UpdateFieldsAsync(
                It.IsAny<int>(),
                It.Is<IReadOnlyList<WorkItemField>>(f =>
                    f.Any(x => x.ReferenceName == "System.Description" &&
                               x.Value != null &&
                               x.Value.ToString()!.Contains(expectedTarget) &&
                               !x.Value.ToString()!.Contains(expectedOriginal))),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Then("the updated field values are applied to the target work item")]
    public void ThenUpdatedFieldValuesAreApplied()
    {
        _ctx.MockTarget.Verify(
            t => t.UpdateFieldsAsync(It.IsAny<int>(), It.IsAny<IReadOnlyList<WorkItemField>>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // ── Scenario 2: processing skipped when extension disabled ────────────────

    [Given("a revision contains embedded images")]
    public void GivenARevisionContainsEmbeddedImages()
    {
        const string originalUrl = "https://source.example.com/api/attachments/img2.png";
        _ctx.OriginalUrl = originalUrl;
        _ctx.RevisionJson = $$"""
        {
          "WorkItemId": 1,
          "RevisionIndex": 0,
          "Fields": [
            {"ReferenceName": "System.WorkItemType", "Value": "Task"},
            {"ReferenceName": "System.Description", "Value": "<img src=\"{{originalUrl}}\">" }
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
    }

    [Given("the EmbeddedImages extension is set to disabled")]
    public void GivenEmbeddedImagesExtensionIsDisabled()
    {
        _ctx.Extensions = new WorkItemsModuleExtensions
        {
            EmbeddedImages = new EmbeddedImagesExtensionOptions { Enabled = false }
        };
    }

    [When("the import processes Stage B for that revision")]
    public async Task WhenTheImportProcessesStageBForThatRevision()
    {
        _ctx.SetupMocks();
        var processor = _ctx.BuildProcessor();
        await processor.ProcessAsync(
            "WorkItems/2024-01-01/00000638000000000001-1-0",
            _ctx.Extensions,
            resumeAtStage: null,
            _ctx.MockResolutionStrategy.Object,
            CancellationToken.None);
    }

    [Then("no embedded images are uploaded")]
    public void ThenNoEmbeddedImagesAreUploaded()
    {
        _ctx.MockTarget.Verify(
            t => t.UploadEmbeddedImageAsync(It.IsAny<string>(), It.IsAny<System.IO.Stream>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Then("field values are applied to the target without URL rewriting")]
    public void ThenFieldValuesAreAppliedWithoutUrlRewriting()
    {
        var expectedOriginal = _ctx.OriginalUrl!;
        // The original URL should still be in the field value (no rewrite).
        _ctx.MockTarget.Verify(
            t => t.UpdateFieldsAsync(
                It.IsAny<int>(),
                It.Is<IReadOnlyList<WorkItemField>>(f =>
                    f.Any(x => x.ReferenceName == "System.Description" &&
                               x.Value != null &&
                               x.Value.ToString()!.Contains(expectedOriginal))),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
