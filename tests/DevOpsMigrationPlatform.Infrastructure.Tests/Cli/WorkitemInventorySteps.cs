using Reqnroll;

namespace DevOpsMigrationPlatform.Infrastructure.Tests.Cli;

[Binding]
[Scope(Feature = "Discovery Inventory CLI Command")]
public class WorkitemInventorySteps
{
    [Given("the devopsmigration CLI is installed and on the PATH")]
    public void Background1() => throw new PendingStepException();

    [Given("the operator provides a valid organisation URL and Personal Access Token")]
    public void Background2() => throw new PendingStepException();

    [Given("the organisation contains projects {string}, {string}, and {string}")]
    public void GivenOrgContainsProjects(string p1, string p2, string p3) => throw new PendingStepException();

    [When("the operator runs {string}")]
    public void WhenOperatorRunsCommand(string command) => throw new PendingStepException();

    [Then("a table is rendered in the terminal before any counting begins")]
    public void ThenTableRenderedBeforeCounting() => throw new PendingStepException();

    [Then("the table gains a row for each project as its row is first populated")]
    public void ThenTableGainsRowPerProject() => throw new PendingStepException();

    [Then("the Work Items and Revisions columns update in place for each project as counting progresses")]
    public void ThenColumnsUpdateInPlace() => throw new PendingStepException();

    [Given("any organisation with at least one project")]
    public void GivenOrgWithAtLeastOneProject() => throw new PendingStepException();

    [When("the discovery inventory command runs")]
    public void WhenDiscoveryInventoryCommandRuns() => throw new PendingStepException();

    [Then("the rendered table has columns: {string}, {string}, {string}, {string}, {string}, and {string}")]
    public void ThenTableHasColumns(string c1, string c2, string c3, string c4, string c5, string c6) => throw new PendingStepException();

    [Given("the discovery inventory command is streaming results for {string}")]
    public void GivenCommandStreamingResultsForProject(string project) => throw new PendingStepException();

    [When("a new work item count update arrives")]
    public void WhenNewWorkItemCountArrives() => throw new PendingStepException();

    [Then("the {string} cell for {string} shows a time value in HH:mm:ss format reflecting when that update was received")]
    public void ThenCellShowsTime(string column, string project) => throw new PendingStepException();

    [Given("the discovery inventory command has finished counting all projects")]
    public void GivenCommandFinishedCounting() => throw new PendingStepException();

    [Then("a file named {string} is created in the current working directory")]
    public void ThenFileCreatedInCwd(string fileName) => throw new PendingStepException();

    [Then("the CSV contains one row per project with columns for name, work item count, revision count, repo count, and pipeline count")]
    public void ThenCsvContainsCorrectColumns() => throw new PendingStepException();

    [Then("the terminal displays a success message confirming the file path")]
    public void ThenTerminalDisplaysSuccess() => throw new PendingStepException();

    [Given("the Azure DevOps organisation has no projects")]
    public void GivenOrgHasNoProjects() => throw new PendingStepException();

    [When("the operator runs the discovery inventory command")]
    public void WhenOperatorRunsDiscoveryCommand() => throw new PendingStepException();

    [Then("an empty table is displayed")]
    public void ThenEmptyTableDisplayed() => throw new PendingStepException();

    [Then("the command exits with exit code {int}")]
    public void ThenCommandExitsWithCode(int code) => throw new PendingStepException();

    [Then("an empty {string} file is created with only the CSV header row")]
    public void ThenEmptyCsvCreatedWithHeader(string fileName) => throw new PendingStepException();

    [Given("the supplied Personal Access Token is not valid")]
    public void GivenInvalidPat() => throw new PendingStepException();

    [Then("the command exits with a non-zero exit code")]
    public void ThenCommandExitsWithNonZeroCode() => throw new PendingStepException();

    [Then("the terminal displays an error message describing the authentication failure")]
    public void ThenTerminalShowsAuthError() => throw new PendingStepException();

    [Given("the organisation has {int} projects")]
    public void GivenOrgHasProjects(int count) => throw new PendingStepException();

    [When("the discovery inventory command is running")]
    public void WhenDiscoveryCommandIsRunning() => throw new PendingStepException();

    [Then("each project is counted to completion before the next project counting begins")]
    public void ThenProjectsCountedSequentially() => throw new PendingStepException();

    [Then("the live table shows the final counts for earlier projects while later projects are still being counted")]
    public void ThenLiveTableShowsFinalCountsForEarlierProjects() => throw new PendingStepException();
}
