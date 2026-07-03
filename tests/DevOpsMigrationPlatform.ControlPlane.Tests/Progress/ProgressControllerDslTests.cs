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
    [TestCategory("IntegrationTests")]
    [TestMethod]
    public async Task GetProgress_WhenEventPosted_Returns200WithEvent()
    {
        var ctx = new ProgressControllerContext();
        ctx.Store.Append(s_postJobId, ctx.MakeEvent("PostedStage"));

        ctx.SetAuthenticatedUser();
        await ctx.Controller.GetProgress(s_postJobId, follow: false, CancellationToken.None);

        Assert.AreEqual(200, ctx.Controller.HttpContext.Response.StatusCode);
        ctx.Controller.HttpContext.Response.Body.Seek(0, SeekOrigin.Begin);
        var body = await new StreamReader(ctx.Controller.HttpContext.Response.Body).ReadToEndAsync();
        Assert.IsTrue(body.Contains("PostedStage"), "Response body should contain the posted stage.");
    }

    // ── Scenario: 403 when caller lacks job visibility ────────────────────────

    [TestCategory("CodeTest")]
    [TestCategory("IntegrationTests")]
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
