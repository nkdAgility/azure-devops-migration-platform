using Reqnroll;

namespace DevOpsMigrationPlatform.Infrastructure.Tests.Export;

[Binding]
[Scope(Feature = "Identities Module Export")]
public class ExportIdentitiesSteps
{
    [Given("the source project contains users {string} and {string}")]
    public void GivenSourceContainsUsers(string user1, string user2) => throw new PendingStepException();

    [When("the identities export runs")]
    public void WhenIdentitiesExportRuns() => throw new PendingStepException();

    [Then("{string} is written to the package")]
    public void ThenFileIsWrittenToPackage(string path) => throw new PendingStepException();

    [Then("it contains entries for both {string} and {string}")]
    public void ThenFileContainsBothEntries(string user1, string user2) => throw new PendingStepException();
}
