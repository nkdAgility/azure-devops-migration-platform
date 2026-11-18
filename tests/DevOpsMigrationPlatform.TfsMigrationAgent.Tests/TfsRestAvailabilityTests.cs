// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using DevOpsMigrationPlatform.Abstractions.Agent.Context;
using DevOpsMigrationPlatform.Infrastructure.TfsObjectModel;
using DevOpsMigrationPlatform.Infrastructure.TfsObjectModel.ProjectLifecycle;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DevOpsMigrationPlatform.TfsMigrationAgent.Tests;

/// <summary>
/// Martin, 2026-11-18: the TFS-target gaps without REST are acceptable and just need handling.
/// A TFS target with no REST API (e.g. TFS 2008) cannot have its project created by the
/// platform, so the project must exist; the operator is told which project to create.
/// </summary>
[TestClass]
public sealed class TfsRestAvailabilityTests
{
    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public void IsMissing_RecognisesAServerWithoutRest()
    {
        var notFound = new System.Net.Http.HttpRequestException("Response status code does not indicate success: 404 (Not Found).");

        Assert.IsTrue(TfsRestAvailability.IsMissing(new Microsoft.VisualStudio.Services.WebApi.VssResourceNotFoundException(Guid.NewGuid())));
        Assert.IsTrue(TfsRestAvailability.IsMissing(notFound));
        Assert.IsFalse(TfsRestAvailability.IsMissing(new System.Net.Http.HttpRequestException("503 (Service Unavailable)")));
    }

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public void ProjectCreate_OnAServerWithoutRest_TellsTheOperatorToCreateTheProject()
    {
        var translated = TfsProjectLifecycleProvider.TranslateCreateFailure(
            new Microsoft.VisualStudio.Services.WebApi.VssResourceNotFoundException(Guid.NewGuid()), "Contoso");

        var configuration = translated as JobConfigurationException;
        Assert.IsNotNull(configuration, "A server without REST must give an operator message, not a raw SDK error.");
        StringAssert.Contains(configuration.Message, "Contoso");
        StringAssert.Contains(configuration.Message, "create");
    }

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public void ProjectCreate_OtherFailure_IsNotTranslated()
    {
        Assert.IsNull(TfsProjectLifecycleProvider.TranslateCreateFailure(new InvalidOperationException("boom"), "Contoso"));
    }
}
