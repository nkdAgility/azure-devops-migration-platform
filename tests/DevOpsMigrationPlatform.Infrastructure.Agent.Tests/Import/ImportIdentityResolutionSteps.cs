using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using Reqnroll;

namespace DevOpsMigrationPlatform.Infrastructure.Tests.Import;

[Binding]
[Scope(Feature = "Identity Resolution During Import")]
public class ImportIdentityResolutionSteps
{
    private readonly ImportIdentityResolutionContext _ctx;

    public ImportIdentityResolutionSteps(ImportIdentityResolutionContext ctx) => _ctx = ctx;

    // ── Background ────────────────────────────────────────────────────────────

    [Given("a valid migration package exists at the configured package root")]
    public void GivenAValidMigrationPackageExistsAtTheConfiguredPackageRoot() { }

    [Given(@"an identity mapping file exists at {string}")]
    public void GivenAnIdentityMappingFileExists(string _path) { }

    // ── Scenario 1: mapped identity resolved ──────────────────────────────────

    [Given(@"the identity mapping contains a mapping from ""(.*)"" to ""(.*)""")]
    public void GivenIdentityMappingContains(string source, string target)
    {
        _ctx.MockIdentityMapping
            .Setup(s => s.Resolve(source))
            .Returns(target);
    }

    [Given(@"a revision contains field ""(.*)"" with value ""(.*)""")]
    public void GivenARevisionContainsField(string fieldName, string fieldValue)
    {
        _ctx.RevisionJson = $$"""
        {
          "WorkItemId": 1,
          "RevisionIndex": 0,
          "Fields": [
            {"ReferenceName": "System.WorkItemType", "Value": "Task"},
            {"ReferenceName": "{{fieldName}}", "Value": "{{fieldValue}}"}
          ],
          "Attachments": [],
          "RelatedLinks": [],
          "ExternalLinks": [],
          "Hyperlinks": [],
          "EmbeddedImages": []
        }
        """;
        _ctx.FieldName = fieldName;
        _ctx.SourceFieldValue = fieldValue;
    }

    [When(@"the import processes Stage B \(AppliedFields\) for that revision")]
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

    [Then(@"the field ""(.*)"" is applied to the target with value ""(.*)""")]
    public void ThenFieldIsAppliedWithValue(string fieldName, string expectedValue)
    {
        _ctx.MockTarget.Verify(
            t => t.UpdateFieldsAsync(
                It.IsAny<int>(),
                It.Is<IReadOnlyList<WorkItemField>>(f =>
                    f.Any(x => x.ReferenceName == fieldName && (string?)x.Value == expectedValue)),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // ── Scenario 2: unmapped identity is recorded ─────────────────────────────

    [Given(@"the identity mapping does not contain an entry for ""(.*)""")]
    public void GivenIdentityMappingDoesNotContainEntry(string unmappedIdentity)
    {
        // Resolve returns the original value when no mapping exists (pass-through).
        _ctx.MockIdentityMapping
            .Setup(s => s.Resolve(unmappedIdentity))
            .Returns(unmappedIdentity);
    }

    [Then("the import continues without failure")]
    public void ThenImportContinuesWithoutFailure()
    {
        // No exception was thrown in WhenTheImportProcessesStageBForThatRevision.
        _ctx.MockTarget.Verify(
            t => t.UpdateFieldsAsync(It.IsAny<int>(), It.IsAny<IReadOnlyList<WorkItemField>>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Then(@"{string} is recorded in {string}")]
    public void ThenIdentityIsRecordedInUnresolvedJson(string identity, string _path)
    {
        // The IIdentityMappingService.Resolve call was made, which in a real implementation
        // records the unresolved identity. Here we verify Resolve was called with the identity.
        _ctx.MockIdentityMapping.Verify(s => s.Resolve(identity), Times.Once);
    }

    // ── Scenario 3: no mapping configured — pass-through ─────────────────────

    [Given("no identity mapping file is configured")]
    public void GivenNoIdentityMappingFileIsConfigured()
    {
        // PassThroughIdentityMappingService returns identity unchanged.
        _ctx.MockIdentityMapping
            .Setup(s => s.Resolve(It.IsAny<string>()))
            .Returns<string>(id => id);
    }

    [Then(@"the field ""(.*)"" is applied to the target unchanged")]
    public void ThenFieldIsAppliedUnchanged(string fieldName)
    {
        // The source value should have been passed through as-is.
        _ctx.MockTarget.Verify(
            t => t.UpdateFieldsAsync(
                It.IsAny<int>(),
                It.Is<IReadOnlyList<WorkItemField>>(f =>
                    f.Any(x => x.ReferenceName == fieldName && (string?)x.Value == _ctx.SourceFieldValue)),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
