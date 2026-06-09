// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace DevOpsMigrationPlatform.ControlPlane.Tests.Progress;

[TestClass]
public class ProgressControllerDslTests
{
    private static readonly Guid s_postJobId = new("22222222-2222-2222-2222-222222222222");
    private static readonly Guid s_existingJobId = new("33333333-3333-3333-3333-333333333333");

    // ── Scenario: ProgressEvent is retrievable via GET /jobs/{id}/logs ────────

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public async Task GetProgress_WhenEventPosted_Returns200WithEvent()
    {
        var ctx = new ProgressControllerContext();
        var leaseId = "lease-" + s_postJobId;
        ctx.LeaseResolver.Setup(r => r.ResolveJobId(leaseId)).Returns(s_postJobId);
        ctx.Controller.PostProgress(leaseId, ctx.MakeEvent("PostedStage"));

        ctx.SetAuthenticatedUser();
        await ctx.Controller.GetProgress(s_postJobId, follow: false, CancellationToken.None);

        Assert.AreEqual(200, ctx.Controller.HttpContext.Response.StatusCode);
        ctx.Controller.HttpContext.Response.Body.Seek(0, SeekOrigin.Begin);
        var body = await new StreamReader(ctx.Controller.HttpContext.Response.Body).ReadToEndAsync();
        Assert.IsTrue(body.Contains("PostedStage"), "Response body should contain the posted stage.");
    }

    // ── Scenario: 404 when lease is not recognised ────────────────────────────

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public void PostProgress_UnknownLease_Returns404()
    {
        var ctx = new ProgressControllerContext();
        ctx.LeaseResolver.Setup(r => r.ResolveJobId("unknown-lease")).Returns((Guid?)null);

        var result = ctx.Controller.PostProgress("unknown-lease", ctx.MakeEvent("Stage"));

        var status = (result as StatusCodeResult)?.StatusCode ?? (result as ObjectResult)?.StatusCode;
        Assert.AreEqual(404, status);
    }

    // ── Scenario: 403 when caller lacks job visibility ────────────────────────

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public async Task GetProgress_UnauthenticatedCaller_Returns403()
    {
        var ctx = new ProgressControllerContext();
        ctx.Store.Append(s_existingJobId, ctx.MakeEvent("Existing"));
        ctx.SetUnauthenticatedUser();

        await ctx.Controller.GetProgress(s_existingJobId, follow: false, CancellationToken.None);

        Assert.AreEqual(403, ctx.Controller.HttpContext.Response.StatusCode);
    }
}
