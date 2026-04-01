using Reqnroll;

namespace DevOpsMigrationPlatform.Infrastructure.Tests.Import;

[Binding]
[Scope(Feature = "Streaming Import Replay")]
public class StreamingReplaySteps
{
    [Given("a valid migration package exists at the configured package root")]
    public void Background1() => throw new PendingStepException();

    [Given("the package contains work item revision folders in canonical chronological order")]
    public void Background2() => throw new PendingStepException();

    [Given("the package contains revision folders in lexicographic order")]
    public void GivenPackageHasLexicographicFolders() => throw new PendingStepException();

    [When("the WorkItems import module runs")]
    public void WhenImportModuleRuns() => throw new PendingStepException();

    [Then("each revision is applied to the target in the order determined by folder name ascending")]
    public void ThenRevisionsAppliedInOrder() => throw new PendingStepException();

    [Then("the order of application matches the package order without any reordering step")]
    public void ThenOrderMatchesPackageWithoutReorder() => throw new PendingStepException();

    [Given("the package contains {int} revision folders")]
    public void GivenPackageContainsRevisionFolders(int count) => throw new PendingStepException();

    [Then("revisions are enumerated and applied one folder at a time")]
    public void ThenRevisionsEnumeratedOneAtATime() => throw new PendingStepException();

    [Then("the import does not require all revisions to be known before processing begins")]
    public void ThenImportDoesNotRequireAllRevisions() => throw new PendingStepException();

    [Given("a revision folder contains a {string} with title, state, and assigned-to fields")]
    public void GivenRevisionFolderContainsJsonWithFields(string file) => throw new PendingStepException();

    [When("the WorkItems import module processes that revision folder")]
    public void WhenImportProcessesRevisionFolder() => throw new PendingStepException();

    [Then("the target work item is updated with the title, state, and assigned-to from revision.json")]
    public void ThenTargetUpdatedWithFields() => throw new PendingStepException();

    [Given("a revision.json contains an {string} field with a source identity")]
    public void GivenRevisionJsonContainsAssignedTo(string field) => throw new PendingStepException();

    [When("the WorkItems import module applies the revision")]
    public void WhenImportModuleAppliesRevision() => throw new PendingStepException();

    [Then("the assigned-to value is resolved via the configured identity mapping")]
    public void ThenAssignedToResolvedViaMapping() => throw new PendingStepException();

    [Then("no direct identity lookup is performed during revision application")]
    public void ThenNoDirectIdentityLookup() => throw new PendingStepException();

    [Given("the import module is processing a revision folder")]
    public void GivenImportModuleProcessingRevisionFolder() => throw new PendingStepException();

    [When("the import module applies the revision to the target")]
    public void WhenImportAppliesRevisionToTarget() => throw new PendingStepException();

    [Then("only target-side API calls are made")]
    public void ThenOnlyTargetApiCallsMade() => throw new PendingStepException();

    [Then("the source system is not contacted during import")]
    public void ThenSourceSystemNotContactedDuringImport() => throw new PendingStepException();

    [Given("a revision folder contains {string} and {string}")]
    public void GivenRevisionFolderContainsTwoFiles(string file1, string file2) => throw new PendingStepException();

    [When("the WorkItems import module processes the revision folder")]
    public void WhenImportModuleProcessesRevisionFolder() => throw new PendingStepException();

    [Then("{string} is uploaded to the target work item at the correct revision")]
    public void ThenFileUploadedToTarget(string file) => throw new PendingStepException();

    [Then("the attachment metadata in the target matches the reference in {string}")]
    public void ThenAttachmentMetadataMatchesRevisionJson(string file) => throw new PendingStepException();
}
