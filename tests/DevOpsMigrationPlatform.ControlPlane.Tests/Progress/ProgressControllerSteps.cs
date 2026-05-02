// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.ControlPlane.Tests.Progress;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Reqnroll;

namespace DevOpsMigrationPlatform.ControlPlane.Tests.Progress;

[Binding]
internal sealed class ProgressControllerSteps
{
    private readonly ProgressControllerContext _ctx;
    private IActionResult? _result;
    private Guid _activeJobId;

    public ProgressControllerSteps(ProgressControllerContext ctx) => _ctx = ctx;

    [Given(@"a ProgressEvent has been POSTed for job ""([^""]+)""")]
    public void GivenAProgressEventHasBeenPosted(string jobIdString)
    {
        _activeJobId = Guid.Parse(jobIdString);
        var leaseId = "lease-" + jobIdString;

        _ctx.LeaseResolver.Setup(r => r.ResolveJobId(leaseId)).Returns(_activeJobId);
        _result = _ctx.Controller.PostProgress(leaseId, _ctx.MakeEvent("PostedStage"));
    }

    [Given(@"there is no active lease for lease id ""([^""]+)""")]
    public void GivenThereIsNoActiveLease(string leaseId)
    {
        _ctx.LeaseResolver.Setup(r => r.ResolveJobId(leaseId)).Returns((Guid?)null);
    }

    [Given(@"a job ""([^""]+)"" exists in the store")]
    public void GivenAJobExistsInTheStore(string jobIdString)
    {
        _activeJobId = Guid.Parse(jobIdString);
        _ctx.Store.Append(_activeJobId, _ctx.MakeEvent("Existing"));
    }

    [Given("the caller does not have permission to view it")]
    [When("the caller does not have permission to view it")]
    public void GivenCallerLacksPermission()
    {
        _ctx.SetUnauthenticatedUser();
    }

    [When(@"the agent POSTs a ProgressEvent to /agents/lease/([^/]+)/progress")]
    public void WhenAgentPostsEvent(string leaseId)
    {
        _result = _ctx.Controller.PostProgress(leaseId, _ctx.MakeEvent("PostedStage"));
    }

    [When(@"I send GET /jobs/([^/]+)/logs")]
    public async Task WhenISendGetLogs(string jobIdString)
    {
        _activeJobId = Guid.TryParse(jobIdString, out var parsed) ? parsed : _activeJobId;
        // Only set authenticated context if no HttpContext has been configured yet
        // (i.e. a Given step hasn't already set an unauthenticated context for a 403 test).
        if (_ctx.Controller.ControllerContext?.HttpContext is null)
            _ctx.SetAuthenticatedUser();
        _result = null; // clear any prior IActionResult so ThenTheResponseStatusIs reads HttpContext
        await _ctx.Controller.GetProgress(_activeJobId, follow: false, CancellationToken.None);
    }

    [Then(@"the response status is (\d+)")]
    public void ThenTheResponseStatusIs(int statusCode)
    {
        if (_result is not null)
        {
            var objectResult = _result as ObjectResult;
            var statusResult = _result as StatusCodeResult;
            var actual = objectResult?.StatusCode ?? statusResult?.StatusCode;
            Assert.AreEqual(statusCode, actual, $"Expected status code {statusCode}.");
        }
        else
        {
            // Response was set directly on HttpContext (GetLogs method)
            Assert.AreEqual(statusCode, _ctx.Controller.HttpContext.Response.StatusCode,
                $"Expected HTTP response status code {statusCode}.");
        }
    }

    [Then("the response body contains the stored ProgressEvent")]
    public async Task ThenTheResponseBodyContainsTheStoredProgressEvent()
    {
        var body = _ctx.Controller.HttpContext.Response.Body;
        body.Seek(0, System.IO.SeekOrigin.Begin);
        var content = await new System.IO.StreamReader(body).ReadToEndAsync();
        Assert.IsTrue(content.Contains("PostedStage"),
            "Response body should contain the stored ProgressEvent stage 'PostedStage'.");
    }
}
