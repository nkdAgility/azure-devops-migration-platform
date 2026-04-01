using Reqnroll;

namespace DevOpsMigrationPlatform.Infrastructure.Tests.Cli;

[Binding]
[Scope(Feature = "TFS Export CLI Command")]
public class TfsExportSteps
{
    [Given("TFS export is available and configured")]
    public void Background1() => throw new PendingStepException();

    [Given("the TFS server at {string} is reachable")]
    public void GivenTfsServerReachable(string url) => throw new PendingStepException();

    [Given("the project {string} exists on that server")]
    public void GivenProjectExistsOnServer(string project) => throw new PendingStepException();

    [Then("the terminal displays a live status showing total work items, processed work items, and processed revisions")]
    public void ThenLiveStatusShown() => throw new PendingStepException();

    [Then("the status updates as each work item is processed")]
    public void ThenStatusUpdatesPerWorkItem() => throw new PendingStepException();

    [Then("on completion the terminal shows a success confirmation")]
    public void ThenSuccessConfirmationShown() => throw new PendingStepException();

    [Then("the command exits with a non-zero exit code")]
    public void ThenNonZeroExitCode() => throw new PendingStepException();

    [Then("the terminal displays a validation error indicating the TFS server must be a valid HTTP or HTTPS URL")]
    public void ThenTfsServerUrlValidationError() => throw new PendingStepException();

    [Then("the terminal displays a validation error indicating a project name must be provided")]
    public void ThenProjectNameValidationError() => throw new PendingStepException();

    [Then("the terminal displays a validation error indicating the output folder must be provided")]
    public void ThenOutputFolderValidationError() => throw new PendingStepException();

    [Given("TFS export is available")]
    public void GivenTfsExportAvailable() => throw new PendingStepException();

    [When("the operator runs the tfsexport command")]
    public void WhenOperatorRunsTfsExportCommand() => throw new PendingStepException();

    [Then("output lines appear in the terminal as the export progresses")]
    public void ThenOutputLinesAppear() => throw new PendingStepException();

    [Then("error output is visually distinguished from standard output in the terminal")]
    public void ThenErrorOutputDistinguished() => throw new PendingStepException();

    [Given("the tfsexport subprocess exits with code {int}")]
    public void GivenSubprocessExitsWithCode(int code) => throw new PendingStepException();

    [When("the tfsexport command is invoked")]
    public void WhenTfsExportCommandInvoked() => throw new PendingStepException();

    [Then("the devopsmigration CLI exits with code {int}")]
    public void ThenCliExitsWithCode(int code) => throw new PendingStepException();

    [Then("the terminal displays an error message indicating the TFS export failed with that exit code")]
    public void ThenErrorMessageForSubprocessFailure() => throw new PendingStepException();

    [Given("TFS export is not available")]
    public void GivenTfsExportNotAvailable() => throw new PendingStepException();

    [Then("the terminal displays an error message explaining that TFS export could not be started")]
    public void ThenTfsExportCouldNotBeStartedError() => throw new PendingStepException();

    [Given("a project with work items spread across multiple date chunks")]
    public void GivenProjectWithWorkItemsInChunks() => throw new PendingStepException();

    [When("the tfsexport command is running")]
    public void WhenTfsExportCommandRunning() => throw new PendingStepException();

    [Then("the live status shows the current chunk start date, chunk end date, and the number of work items within that chunk")]
    public void ThenLiveStatusShowsChunkInfo() => throw new PendingStepException();
}
