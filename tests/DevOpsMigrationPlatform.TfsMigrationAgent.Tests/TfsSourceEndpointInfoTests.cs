// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Options;
using DevOpsMigrationPlatform.Infrastructure.TfsObjectModel;
using DevOpsMigrationPlatform.Infrastructure.TfsObjectModel.Platform.Configuration;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DevOpsMigrationPlatform.TfsMigrationAgent.Tests;

/// <summary>
/// #169 live E2E: the TFS agent's source endpoint view dropped the credentials, so embedded images
/// were requested anonymously and Azure DevOps returned its sign-in page (203 text/html).
/// </summary>
[TestClass]
public class TfsSourceEndpointInfoTests
{
    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public void ToOrganisationEndpoint_CarriesTheSourceCredentials()
    {
        Environment.SetEnvironmentVariable("E2E169_TFS_TOKEN", "tok-169");
        try
        {
            var options = new TeamFoundationServerEndpointOptions
            {
                Url = "https://dev.azure.com/source-org",
                Project = "SourceProject",
                Authentication = new EndpointAuthenticationOptions
                {
                    Type = AuthenticationType.AccessToken,
                    AccessToken = "$ENV:E2E169_TFS_TOKEN"
                }
            };
            var sut = new TfsObjectModelModuleServiceExtensions.DeferredTfsSourceEndpointInfo(() => options);

            var endpoint = sut.ToOrganisationEndpoint();

            Assert.AreEqual("https://dev.azure.com/source-org", sut.Url);
            Assert.AreEqual("SourceProject", sut.Project);
            Assert.AreEqual(AuthenticationType.AccessToken, endpoint.Authentication.Type);
            Assert.AreEqual("tok-169", endpoint.Authentication.ResolvedAccessToken);
        }
        finally
        {
            Environment.SetEnvironmentVariable("E2E169_TFS_TOKEN", null);
        }
    }

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public void NoActiveJob_IsEmpty()
    {
        var sut = new TfsObjectModelModuleServiceExtensions.DeferredTfsSourceEndpointInfo(() => null);

        Assert.AreEqual(string.Empty, sut.Url);
        Assert.IsNull(sut.ToOrganisationEndpoint().Authentication.ResolvedAccessToken);
    }
}
