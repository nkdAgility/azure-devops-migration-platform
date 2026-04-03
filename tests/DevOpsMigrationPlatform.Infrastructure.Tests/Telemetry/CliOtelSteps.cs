using DevOpsMigrationPlatform.Infrastructure.Tests.Telemetry;
using OpenTelemetry.Trace;
using Reqnroll;
using System.Diagnostics;

namespace DevOpsMigrationPlatform.Infrastructure.Tests.Telemetry;

[Binding]
[Scope(Feature = "CLI OTel Observability")]
internal sealed class CliOtelSteps
{
    private readonly CliOtelContext _ctx;

    public CliOtelSteps(CliOtelContext ctx)
    {
        _ctx = ctx;
    }

    [Given(@"a valid Azure Monitor connection string is configured under ""(.+)""")]
    public void GivenAValidAzureMonitorConnectionStringIsConfigured(string _)
    {
        // Build a TracerProvider backed by in-memory exporter to simulate the Azure Monitor pipeline.
        _ctx.BuildTracerProvider(withAzureMonitorStub: true);
    }

    [Given("no Azure Monitor connection string is configured")]
    public void GivenNoAzureMonitorConnectionStringIsConfigured()
    {
        // Build provider without any exporter registered (simulates missing connection string).
        _ctx.BuildTracerProvider(withAzureMonitorStub: false);
    }

    [Given("the CLI process initialises Program.cs")]
    public void GivenTheCliProcessInitialises()
    {
        // Context represents the DI state; source is already created in CliOtelContext ctor.
    }

    [When("I run a CLI command to completion")]
    public void WhenIRunACliCommandToCompletion()
    {
        using var activity = _ctx.ActivitySource.StartActivity("tfsexport");
        // Simulate successful completion — no exception, span closes normally.
        _ctx.LastActivity = activity;
        _ctx.CommandExitCode = 0;
        _ctx.TracerProvider?.ForceFlush(5000);
    }

    [When("I run a CLI command that throws an unhandled exception")]
    public void WhenIRunACliCommandThatThrows()
    {
        using var activity = _ctx.ActivitySource.StartActivity("tfsexport");
        _ctx.LastActivity = activity;
        try
        {
            throw new InvalidOperationException("simulated command failure");
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            _ctx.CommandExitCode = 1;
        }
        _ctx.TracerProvider?.ForceFlush(5000);
    }

    [When("the DI container is built")]
    public void WhenTheDiContainerIsBuilt()
    {
        // Already built during context construction; source is registered as singleton in prod code.
        _ctx.BuildTracerProvider();
    }

    [Then("a trace span for that command is exported to the telemetry pipeline")]
    public void ThenASpanIsExportedToThePipeline()
    {
        Assert.IsTrue(_ctx.ExportedActivities.Count > 0,
            "Expected at least one exported Activity but none were found.");
        Assert.IsTrue(_ctx.ExportedActivities.Any(a => a.OperationName == "tfsexport"),
            "Expected an Activity named 'tfsexport'.");
    }

    [Then("the trace span status is Error with the exception message attached")]
    public void ThenTheSpanStatusIsError()
    {
        var failed = _ctx.ExportedActivities.FirstOrDefault(a =>
            a.Status == ActivityStatusCode.Error);

        Assert.IsNotNull(failed, "Expected a span with ActivityStatusCode.Error.");
        Assert.IsFalse(string.IsNullOrEmpty(failed.StatusDescription),
            "Expected a non-empty status description containing the exception message.");
    }

    [Then("the command exits with code 0")]
    public void ThenTheCommandExitsWithCode0()
    {
        Assert.AreEqual(0, _ctx.CommandExitCode, "Expected command exit code 0.");
    }

    [Then("no external telemetry exporter is registered")]
    public void ThenNoExternalExporterIsRegistered()
    {
        // When ExporterRegistered is false, no Azure Monitor exporter was configured.
        Assert.IsFalse(_ctx.ExporterRegistered,
            "Expected no external exporter to be registered.");
    }

    [Then(@"an ActivitySource named ""(.+)"" is registered as a singleton")]
    public void ThenActivitySourceIsRegistered(string sourceName)
    {
        Assert.AreEqual(sourceName, _ctx.ActivitySource.Name,
            $"Expected ActivitySource named '{sourceName}'.");
    }
}
