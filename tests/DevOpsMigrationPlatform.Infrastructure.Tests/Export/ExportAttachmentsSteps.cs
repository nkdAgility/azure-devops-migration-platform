using Reqnroll;

namespace DevOpsMigrationPlatform.Infrastructure.Tests.Export;

[Binding]
[Scope(Feature = "Export Attachments")]
public class ExportAttachmentsSteps
{
    [Given("the source project contains work items with file attachments")]
    public void Background1() => throw new PendingStepException();

    [Given("revision {int} of work item {int} has an attachment named {string}")]
    public void GivenRevisionHasAttachment(int rev, int id, string name) => throw new PendingStepException();

    [Then("{string} is stored at {string}")]
    public void ThenFileStoredAt(string file, string path) => throw new PendingStepException();

    [Then("{string} in the same folder references {string} by relative path")]
    public void ThenRevisionJsonReferencesFileByRelativePath(string revJson, string file) => throw new PendingStepException();

    [Given("any work item with attachments is exported")]
    public void GivenAnyWorkItemWithAttachments() => throw new PendingStepException();

    [Then("no directory named {string} exists at the package root")]
    public void ThenNoDirectoryExistsAtPackageRoot(string dir) => throw new PendingStepException();

    [Then("no directory named {string} exists at the {string} level")]
    public void ThenNoDirectoryExistsAtLevel(string dir, string level) => throw new PendingStepException();

    [Given("revision {int} of work item {int} has {int} attachments: {string}, {string}, and {string}")]
    public void GivenRevisionHasThreeAttachments(int rev, int id, int count, string a, string b, string c) => throw new PendingStepException();

    [Then("{string} contains {string}, {string}, and {string}")]
    public void ThenFolderContainsThreeFiles(string folder, string a, string b, string c) => throw new PendingStepException();

    [Then("{string} lists all three attachments")]
    public void ThenRevisionJsonListsAllAttachments(string file) => throw new PendingStepException();

    [Given("revision {int} of work item {int} has no attachments")]
    public void GivenRevisionHasNoAttachments(int rev, int id) => throw new PendingStepException();

    [Then("{string} contains only {string}")]
    public void ThenFolderContainsOnly(string folder, string file) => throw new PendingStepException();

    [Given("a revision with an attachment")]
    public void GivenARevisionWithAnAttachment() => throw new PendingStepException();

    [When("the attachment is exported")]
    public void WhenTheAttachmentIsExported() => throw new PendingStepException();

    [Then("the attachment binary is written into the package at the correct revision path")]
    public void ThenAttachmentBinaryWrittenAtCorrectPath() => throw new PendingStepException();

    [Then("no attachment files are created outside the package folder hierarchy")]
    public void ThenNoAttachmentFilesOutsidePackage() => throw new PendingStepException();
}
