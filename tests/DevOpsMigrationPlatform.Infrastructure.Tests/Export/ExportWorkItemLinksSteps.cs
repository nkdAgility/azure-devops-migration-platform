using Reqnroll;

namespace DevOpsMigrationPlatform.Infrastructure.Tests.Export;

[Binding]
[Scope(Feature = "Export Work Item Links")]
public class ExportWorkItemLinksSteps
{
    [Given("the source project contains work items with one or more link types")]
    public void Background1() => throw new PendingStepException();

    [Given("work item {int} has {int} revisions")]
    public void GivenWorkItemHasRevisions(int id, int count) => throw new PendingStepException();

    [Given("revision {int} has no links")]
    public void GivenRevisionHasNoLinks(int rev) => throw new PendingStepException();

    [Given("revision {int} adds a related link to work item {int}")]
    public void GivenRevisionAddsRelatedLink(int rev, int targetId) => throw new PendingStepException();

    [When("the WorkItems export module processes work item {int}")]
    public void WhenExportProcessesWorkItem(int id) => throw new PendingStepException();

    [Then("revision {int}'s {string} contains an empty links collection")]
    public void ThenRevisionHasEmptyLinks(int rev, string file) => throw new PendingStepException();

    [Then("revision {int}'s {string} contains exactly the new related link to work item {int}")]
    public void ThenRevisionHasExactlyOneRelatedLink(int rev, string file, int targetId) => throw new PendingStepException();

    [Given("revision {int} of work item {int} adds an external link with uri {string} and type {string}")]
    public void GivenRevisionAddsExternalLink(int rev, int id, string uri, string type) => throw new PendingStepException();

    [When("the WorkItems export module processes revision {int} of work item {int}")]
    public void WhenExportProcessesRevisionOfWorkItem(int rev, int id) => throw new PendingStepException();

    [Then("{string} contains an external link entry with linkedArtifactUri {string}")]
    public void ThenRevisionJsonHasExternalLinkUri(string file, string uri) => throw new PendingStepException();

    [Then("the external link entry records artifactLinkType {string}")]
    public void ThenExternalLinkHasArtifactLinkType(string type) => throw new PendingStepException();

    [Given("revision {int} of work item {int} adds a related link to work item {int} with link type end {string}")]
    public void GivenRevisionAddsRelatedLinkWithType(int rev, int id, int targetId, string linkTypeEnd) => throw new PendingStepException();

    [Then("{string} contains a related link entry with relatedWorkItemId {int}")]
    public void ThenRevisionJsonHasRelatedLink(string file, int relatedId) => throw new PendingStepException();

    [Then("the related link entry records linkTypeEnd {string}")]
    public void ThenRelatedLinkHasLinkTypeEnd(string linkTypeEnd) => throw new PendingStepException();

    [Given("revision {int} of work item {int} adds a hyperlink to {string}")]
    public void GivenRevisionAddsHyperlink(int rev, int id, string url) => throw new PendingStepException();

    [Then("{string} contains a hyperlink entry with location {string}")]
    public void ThenRevisionJsonHasHyperlink(string file, string url) => throw new PendingStepException();

    [Given("revision {int} of work item {int} retains that same related link without adding any new link")]
    public void GivenRevisionRetainsSameLink(int rev, int id) => throw new PendingStepException();

    [Given("revision {int} of work item {int} simultaneously adds one external link, one related link, and one hyperlink")]
    public void GivenRevisionAddsThreeLinkTypes(int rev, int id) => throw new PendingStepException();

    [Then("{string} contains exactly one external link entry")]
    public void ThenRevisionHasOneExternalLink(string file) => throw new PendingStepException();

    [Then("{string} contains exactly one related link entry")]
    public void ThenRevisionHasOneRelatedLink(string file) => throw new PendingStepException();

    [Then("{string} contains exactly one hyperlink entry")]
    public void ThenRevisionHasOneHyperlink(string file) => throw new PendingStepException();

    [Given("revision {int} of work item {int} contains a link of an unsupported type")]
    public void GivenRevisionHasUnsupportedLinkType(int rev, int id) => throw new PendingStepException();

    [Then("the export stops with a clear error identifying the unrecognised link type")]
    public void ThenExportStopsWithError() => throw new PendingStepException();

    [Then("no {string} is written for that revision")]
    public void ThenNoFileWrittenForRevision(string file) => throw new PendingStepException();

    [Given("a revision adds {int} links of different types")]
    public void GivenRevisionAddsLinks(int count) => throw new PendingStepException();

    [Then("the platform records a successful export metric for each link")]
    public void ThenPlatformRecordsMetricForEachLink() => throw new PendingStepException();

    [Then("the platform records the processing duration for each link")]
    public void ThenPlatformRecordsDurationForEachLink() => throw new PendingStepException();
}
